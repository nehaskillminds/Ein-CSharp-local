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
        private readonly IErrorMessageExtractionService _errorMessageExtractionService;

        public AutomationOrchestrator(
            IEinFormFiller formFiller,
            IBlobStorageService blobStorageService,
            ISalesforceClient salesforceClient,
            ILogger<AutomationOrchestrator> logger,
            IErrorMessageExtractionService errorMessageExtractionService)
        {
            _formFiller = formFiller ?? throw new ArgumentNullException(nameof(formFiller));
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            _salesforceClient = salesforceClient ?? throw new ArgumentNullException(nameof(salesforceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _errorMessageExtractionService = errorMessageExtractionService ?? throw new ArgumentNullException(nameof(errorMessageExtractionService));
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
                await _salesforceClient.InitializeSalesforceAuthAsync();
                var (automationSuccess, errorMessage, automationPdfUrl) = await _formFiller.RunAutomation(data, jsonData);
                await _blobStorageService.SaveJsonDataSync(jsonData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? new object()));

                if (!automationSuccess)
                {
                    _logger.LogWarning($"Automation failed for record_id: {data.RecordId}. Message: {errorMessage}");
                    jsonData["response_status"] = "fail";
                    string errorMsg = null;
                    if (_formFiller is IRSEinFormFiller irsFiller && irsFiller.Driver != null)
                        errorMsg = _errorMessageExtractionService.ExtractErrorMessage(irsFiller.Driver);
                    else
                        errorMsg = _errorMessageExtractionService.ExtractErrorMessage(errorMessage);
                    // Only set a fallback error message if not already set
                    if (!jsonData.ContainsKey("error_message") || string.IsNullOrWhiteSpace(jsonData["error_message"] as string))
                    {
                        jsonData["error_message"] = !string.IsNullOrWhiteSpace(errorMsg) ? errorMsg : "Unknown failure";
                    }
                    _logger.LogInformation("Final failure JSON before upload: {Json}", JsonSerializer.Serialize(jsonData));
                    var failureCleanName = System.Text.RegularExpressions.Regex.Replace(data.EntityName ?? "UnknownEntity", "[^\\w]", "");
                    var failureJsonBlobName = $"EntityProcess/{data.RecordId}/{failureCleanName}_data.json";
                    await _blobStorageService.UploadJsonAsync(ConvertToNonNullableDictionary(jsonData), failureJsonBlobName, "application/json", ct);
                    return (false, jsonData["error_message"] as string ?? "Automation failed", automationPdfUrl);
                }

                // Extract EIN number from the message if available
                if (!string.IsNullOrEmpty(errorMessage) && System.Text.RegularExpressions.Regex.IsMatch(errorMessage, @"^\d{2}-\d{7}$"))
                {
                    einNumber = errorMessage;
                }

                // Update JSON data with success status
                jsonData["response_status"] = "success";
                if (!string.IsNullOrEmpty(einNumber))
                {
                    jsonData["einNumber"] = einNumber;
                }

                // Save success JSON data
                var successCleanName = System.Text.RegularExpressions.Regex.Replace(data.EntityName ?? "UnknownEntity", "[^\\w]", "");
                var successJsonBlobName = $"EntityProcess/{data.RecordId}/{successCleanName}_data.json";
                await _blobStorageService.UploadJsonAsync(ConvertToNonNullableDictionary(jsonData), successJsonBlobName, "application/json", ct);

                success = true;
                pdfAzureUrl = automationPdfUrl;
                _logger.LogInformation($"Automation completed successfully for record_id: {data.RecordId}, EIN: {einNumber}");
                return (success, einNumber ?? string.Empty, pdfAzureUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"EIN automation failed for record_id: {data.RecordId}");
                jsonData["error_message"] = _errorMessageExtractionService.ExtractErrorMessage(ex.Message);
                jsonData["exception"] = ex.ToString();
                jsonData["traceback"] = ex.StackTrace;
                jsonData["response_status"] = "fail";
                var exceptionCleanName = System.Text.RegularExpressions.Regex.Replace(data.EntityName ?? "UnknownEntity", "[^\\w]", "");
                var exceptionJsonBlobName = $"EntityProcess/{data.RecordId}/{exceptionCleanName}_data.json";
                await _blobStorageService.UploadJsonAsync(ConvertToNonNullableDictionary(jsonData), exceptionJsonBlobName, "application/json", ct);
                try
                {
                    var (failureBlobUrl, failureSuccess) = await _formFiller.CaptureFailurePageAsPdf(data, ct);
                    if (failureSuccess && !string.IsNullOrEmpty(data.RecordId))
                    {
                        await _salesforceClient.NotifyFailureScreenshotUploadToSalesforceAsync(data.RecordId, failureBlobUrl, data.EntityName ?? "UnknownEntity");
                        await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId, "500", "fail", null);
                    }
                }
                catch (Exception captureEx)
                {
                    _logger.LogWarning(captureEx, $"Failed to capture failure PDF for record_id: {data.RecordId}");
                }
                _logger.LogWarning($"Automation failed for record_id: {data.RecordId} with error: {ex.Message}");
                return (false, jsonData["error_message"] as string ?? ex.Message, null);
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
            { "business_address_2", data.BusinessAddress2 },
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
            kvp => kvp.Value ?? string.Empty
        );
    }
}}