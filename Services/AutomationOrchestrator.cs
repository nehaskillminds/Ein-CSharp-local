using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Contrib.WaitAndRetry;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

#nullable enable

namespace EinAutomation.Api.Services
{
    public sealed class AutomationOrchestrator : IAutomationOrchestrator
    {
        private static readonly IReadOnlyList<TimeSpan> _retries = Backoff.ExponentialBackoff(TimeSpan.FromSeconds(2), 5).ToList();
        private readonly IEinFormFiller _formFiller;
        private readonly IBlobStorageService _blobStorageService;
        private readonly ISalesforceClient _salesforceClient;
        private readonly ILogger<AutomationOrchestrator> _logger;

        public AutomationOrchestrator(
            IEinFormFiller formFiller,
            IBlobStorageService blobStorageService,
            ISalesforceClient salesforceClient,
            ILogger<AutomationOrchestrator> logger)
        {
            _formFiller = formFiller ?? throw new ArgumentNullException(nameof(formFiller));
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            _salesforceClient = salesforceClient ?? throw new ArgumentNullException(nameof(salesforceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(bool Success, string EinNumber, string? AzureBlobUrl)> RunAsync(CaseData data, CancellationToken ct)
        {
            if (data == null)
            {
                _logger.LogError("CaseData is null");
                return (false, string.Empty, null);
            }

            _logger.LogInformation($"Starting automation for record_id: {data.RecordId}");
            string? einNumber = null;
            string? pdfAzureUrl = null;
            bool success = false;
            var jsonData = CreateJsonData(data);

            try
            {
                // 1️⃣ Fill IRS form and capture confirmation
                var (fillSuccess, fillEinNumber, confirmationPdfBytes, blobName) = await _formFiller.FillAsync(data, ct);
                if (!fillSuccess)
                {
                    _logger.LogWarning($"Form filling failed for record_id: {data.RecordId}. Error: {fillEinNumber}");
                    await HandleFailureAsync(data, jsonData, confirmationPdfBytes, blobName, "500", ct);
                    return (false, fillEinNumber ?? string.Empty, null);
                }

                einNumber = fillEinNumber;

                // 2️⃣ Upload confirmation PDF to Azure Blob Storage
                var cleanName = System.Text.RegularExpressions.Regex.Replace(data.EntityName ?? "UnknownEntity", "[^\\w]", "");
                blobName ??= $"EntityProcess/{data.RecordId}/{cleanName}-ID-EINConfirmation.pdf";
                
                // Null check for confirmationPdfBytes
                if (confirmationPdfBytes == null)
                {
                    _logger.LogError($"Confirmation PDF bytes are null for record_id: {data.RecordId}");
                    await HandleFailureAsync(data, jsonData, null, blobName, "500", ct);
                    return (false, "Confirmation PDF bytes are null", null);
                }

                pdfAzureUrl = await Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(_retries, (ex, ts, context) => _logger.LogWarning(ex, $"Blob upload retry for {blobName}"))
                    .ExecuteAsync(() => _blobStorageService.UploadAsync(confirmationPdfBytes, blobName, "application/pdf", true, ct));

                _logger.LogInformation($"Confirmation PDF uploaded to Azure: {pdfAzureUrl}");

                // 3️⃣ Notify Salesforce of confirmation PDF
                await _salesforceClient.NotifyContentMigrationAsync(
                    data.RecordId,
                    pdfAzureUrl,
                    $"{cleanName}-ID-EINConfirmation",
                    "pdf",
                    true,
                    ct);

                // 4️⃣ Handle final submission (EIN letter download and extraction)
                var (submitSuccess, finalEinNumber, einLetterPdfBytes, einLetterBlobName) = await _formFiller.FinalSubmitAsync(data, ct);
                if (!submitSuccess)
                {
                    _logger.LogWarning($"Final submission failed for record_id: {data.RecordId}");
                    einLetterBlobName ??= $"EntityProcess/{data.RecordId}/{cleanName}-ID-EINSubmissionFailure.pdf";
                    await HandleFailureAsync(data, jsonData, einLetterPdfBytes, einLetterBlobName, finalEinNumber ?? "500", ct);
                    return (false, finalEinNumber ?? string.Empty, pdfAzureUrl);
                }

                einNumber = finalEinNumber ?? einNumber;

                // 5️⃣ Upload EIN letter PDF
                einLetterBlobName ??= $"EntityProcess/{data.RecordId}/{cleanName}-ID-EINLetter.pdf";
                
                // Null check for einLetterPdfBytes
                if (einLetterPdfBytes == null)
                {
                    _logger.LogError($"EIN letter PDF bytes are null for record_id: {data.RecordId}");
                    await HandleFailureAsync(data, jsonData, null, einLetterBlobName, "500", ct);
                    return (false, "EIN letter PDF bytes are null", pdfAzureUrl);
                }

                var einLetterUrl = await Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(_retries, (ex, ts, context) => _logger.LogWarning(ex, $"Blob upload retry for {einLetterBlobName}"))
                    .ExecuteAsync(() => _blobStorageService.UploadAsync(einLetterPdfBytes, einLetterBlobName, "application/pdf", false, ct));

                _logger.LogInformation($"EIN Letter PDF uploaded to Azure: {einLetterUrl}");

                // 6️⃣ Notify Salesforce of EIN letter
                await _salesforceClient.NotifyContentMigrationAsync(
                    data.RecordId,
                    einLetterUrl,
                    $"{cleanName}-ID-EINLetter",
                    "pdf",
                    false, // HiddenFromClient
                    ct);

                // 7️⃣ Notify Salesforce of success
                await _salesforceClient.UpdateEinStatusAsync(data.RecordId, "success", einNumber, "", ct);

                // 8️⃣ Save JSON data
                jsonData["response_status"] = "success";
                jsonData["einNumber"] = einNumber;
                var jsonBlobName = $"EntityProcess/{data.RecordId}/{cleanName}_data.json";
                await _blobStorageService.UploadJsonAsync(ConvertToNonNullableDictionary(jsonData), jsonBlobName, "application/json", ct);

                success = true;
                return (success, einNumber ?? string.Empty, einLetterUrl ?? pdfAzureUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"EIN automation failed for record_id: {data.RecordId}");
                jsonData["error_message"] = ex.Message;
                jsonData["exception"] = ex.ToString();
                jsonData["traceback"] = ex.StackTrace;
                jsonData["response_status"] = "fail";

                // Save failure JSON
                var cleanName = System.Text.RegularExpressions.Regex.Replace(data.EntityName ?? "UnknownEntity", "[^\\w]", "");
                var jsonBlobName = $"EntityProcess/{data.RecordId}/{cleanName}_data.json";
                await _blobStorageService.UploadJsonAsync(ConvertToNonNullableDictionary(jsonData), jsonBlobName, "application/json", ct);

                // Upload failure PDF if available
                byte[]? failurePdfBytes = null;
                try
                {
                    failurePdfBytes = await _formFiller.CaptureFailurePageAsync(data, ct);
                }
                catch (Exception captureEx)
                {
                    _logger.LogWarning(captureEx, $"Failed to capture failure PDF for record_id: {data.RecordId}");
                }

                if (failurePdfBytes != null)
                {
                    var failureBlobName = $"EntityProcess/{data.RecordId}/{cleanName}-ID-EINSubmissionFailure.pdf";
                    pdfAzureUrl = await Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(_retries, (ex, ts, context) => _logger.LogWarning(ex, $"Blob upload retry for {failureBlobName}"))
                        .ExecuteAsync(() => _blobStorageService.UploadAsync(failurePdfBytes, failureBlobName, "application/pdf", true, ct));

                    await _salesforceClient.NotifyContentMigrationAsync(
                        data.RecordId,
                        pdfAzureUrl,
                        $"{cleanName}-ID-EINSubmissionFailure",
                        "pdf",
                        true, // HiddenFromClient
                        ct);
                }

                // Notify Salesforce of failure
                await _salesforceClient.UpdateEinStatusAsync(data.RecordId, "fail", "", ex.HResult.ToString(), ct);

                return (false, ex.Message, pdfAzureUrl);
            }
            finally
            {
                // Upload ChromeDriver logs
                try
                {
                    var logBytes = await _formFiller.GetBrowserLogsAsync(data, ct);
                    if (logBytes != null)
                    {
                        var logBlobName = $"logs/{data.RecordId}/chromedriver_{DateTime.UtcNow.Ticks}.log";
                        var logUrl = await _blobStorageService.UploadAsync(logBytes, logBlobName, "text/plain", true, ct);
                        _logger.LogInformation($"Uploaded Chrome log to: {logUrl}");
                    }
                }
                catch (Exception logEx)
                {
                    _logger.LogWarning(logEx, $"Failed to upload Chrome logs for record_id: {data.RecordId}");
                }

                // Cleanup
                await _formFiller.CleanupAsync(ct);
            }
        }

        private async Task HandleFailureAsync(CaseData data, Dictionary<string, object?> jsonData, byte[]? pdfBytes, string? blobName, string? errorCode, CancellationToken ct)
        {
            _logger.LogWarning($"Handling failure for record_id: {data.RecordId}, error_code: {errorCode}");
            jsonData["response_status"] = "fail";
            jsonData["irs_reference_number"] = errorCode;

            // Save failure JSON
            var cleanName = System.Text.RegularExpressions.Regex.Replace(data.EntityName ?? "UnknownEntity", "[^\\w]", "");
            var jsonBlobName = $"EntityProcess/{data.RecordId}/{cleanName}_data.json";
            await _blobStorageService.UploadJsonAsync(ConvertToNonNullableDictionary(jsonData), jsonBlobName, "application/json", ct);

            // Upload failure PDF if available
            if (pdfBytes != null && blobName != null)
            {
                blobName = blobName ?? $"EntityProcess/{data.RecordId}/{cleanName}-ID-EINSubmissionFailure.pdf";
                var pdfUrl = await Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(_retries, (ex, ts, context) => _logger.LogWarning(ex, $"Blob upload retry for {blobName}"))
                    .ExecuteAsync(() => _blobStorageService.UploadAsync(pdfBytes, blobName, "application/pdf", true, ct));

                await _salesforceClient.NotifyContentMigrationAsync(
                    data.RecordId,
                    pdfUrl,
                    $"{cleanName}-ID-EINSubmissionFailure",
                    "pdf",
                    true, // HiddenFromClient
                    ct);
            }

            // Notify Salesforce of failure
            await _salesforceClient.UpdateEinStatusAsync(data.RecordId, "fail", "", errorCode, ct);
        }

        private Dictionary<string, object?> CreateJsonData(CaseData data)
        {
            var missingFields = data.GetType()
                .GetProperties()
                .Where(p => p.GetValue(data) == null && p.Name != nameof(CaseData.RecordId))
                .Select(p => p.Name)
                .ToList();

            if (missingFields.Any())
            {
                _logger.LogInformation($"Missing fields detected (using defaults): {string.Join(", ", missingFields)}");
            }

            return new Dictionary<string, object?>
            {
                { "record_id", data.RecordId },
                { "form_type", data.FormType },
                { "entity_name", data.EntityName },
                { "entity_type", data.EntityType },
                { "formation_date", data.FormationDate },
                { "business_category", data.BusinessCategory },
                { "business_description", data.BusinessDescription },
                { "business_address_1", data.BusinessAddress1 },
                { "entity_state", data.EntityState },
                { "business_address_2-equals", data.BusinessAddress2 },
                { "city", data.City },
                { "zip_code", data.ZipCode },
                { "quarter_of_first_payroll", data.QuarterOfFirstPayroll },
                { "entity_state_record_state", data.EntityStateRecordState },
                { "case_contact_name", data.CaseContactName },
                { "ssn_decrypted", data.SsnDecrypted },
                { "proceed_flag", data.ProceedFlag },
                { "entity_members", data.EntityMembers },
                { "locations", data.Locations },
                { "mailing_address", data.MailingAddress },
                { "county", data.County },
                { "trade_name", data.TradeName },
                { "care_of_name", data.CareOfName },
                { "closing_month", data.ClosingMonth },
                { "filing_requirement", data.FilingRequirement },
                { "employee_details", data.EmployeeDetails?.ToDictionary().ToDictionary(k => k.Key, v => v.Value?.ToString()) },
                { "third_party_designee", data.ThirdPartyDesignee?.ToDictionary().ToDictionary(k => k.Key, v => v.Value?.ToString()) },
                { "llc_details", data.LlcDetails?.ToDictionary().ToDictionary(k => k.Key, v => v.Value?.ToString()) },
                { "missing_fields", missingFields.Any() ? missingFields : null },
                { "response_status", null }
            };
        }

        private Dictionary<string, object> ConvertToNonNullableDictionary(Dictionary<string, object?> source)
        {
            return source.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? string.Empty // Convert null values to empty strings
            );
        }
    }
}