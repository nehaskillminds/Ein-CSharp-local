using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EinAutomation.Api.Services.Interfaces;
using System.Threading;

#nullable enable

namespace EinAutomation.Api.Services
{
    public class SalesforceClient : ISalesforceClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SalesforceClient> _logger;

        private string? _accessToken;
        private string? _instanceUrl;
        private DateTime _tokenExpiry;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry;
        public string? InstanceUrl => _instanceUrl;

        public SalesforceClient(HttpClient httpClient, IConfiguration configuration, ILogger<SalesforceClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokenExpiry = DateTime.MinValue;
        }

        public async Task<bool> InitializeSalesforceAuthAsync()
        {
            try
            {
                _logger.LogInformation("Attempting to authenticate with Salesforce...");

                var clientId = _configuration["Salesforce:ClientId"];
                var clientSecret = _configuration["Salesforce:ClientSecret"];
                var username = _configuration["Salesforce:Username"];
                var password = _configuration["Salesforce:Password"];

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || 
                    string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.LogError("Missing Salesforce configuration");
                    return false;
                }

                var formData = new List<KeyValuePair<string, string>>
                {
                    new("grant_type", "password"),
                    new("client_id", clientId),
                    new("client_secret", clientSecret),
                    new("username", username),
                    new("password", password)
                };

                var formContent = new FormUrlEncodedContent(formData);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _httpClient.PostAsync("https://test.salesforce.com/services/oauth2/token", formContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var tokenData = JsonSerializer.Deserialize<SalesforceTokenResponse>(responseContent);

                    if (tokenData?.access_token != null && tokenData.instance_url != null)
                    {
                        _accessToken = tokenData.access_token;
                        _instanceUrl = tokenData.instance_url;
                        _tokenExpiry = DateTime.UtcNow.AddHours(2); // Salesforce tokens typically expire in 2 hours

                        _logger.LogInformation("Salesforce authentication successful. Instance URL: {InstanceUrl}", _instanceUrl);
                        return true;
                    }
                    else
                    {
                        _logger.LogError("Missing token or URL in response: {Response}", responseContent);
                        return false;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("HTTP error during token retrieval: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during authentication");
                return false;
            }
        }

        private async Task<bool> EnsureAuthenticatedAsync()
        {
            if (!IsAuthenticated)
            {
                return await InitializeSalesforceAuthAsync();
            }
            return true;
        }

        public async Task<bool> NotifySalesforceSuccessAsync(string? entityProcessId, string? einNumber)
        {
            if (string.IsNullOrEmpty(entityProcessId) || string.IsNullOrEmpty(einNumber))
            {
                _logger.LogError("Invalid input parameters for NotifySalesforceSuccessAsync: entityProcessId or einNumber is null or empty");
                return false;
            }

            try
            {
                _logger.LogInformation("Notifying success for entity process ID: {EntityProcessId}", entityProcessId);

                if (!await EnsureAuthenticatedAsync())
                {
                    _logger.LogError("Failed to authenticate with Salesforce");
                    return false;
                }

                var url = $"{_instanceUrl}/services/apexrest/service/v2/formautomation/ein/update?entityProcessId={entityProcessId}";
                
                var payload = new
                {
                    entityProcessId,
                    einNumber,
                    errorCode = "",
                    
                    status = "success"
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.PostAsync(url, jsonContent);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Token expired. Refreshing...");
                    if (await InitializeSalesforceAuthAsync())
                    {
                        _httpClient.DefaultRequestHeaders.Clear();
                        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                        response = await _httpClient.PostAsync(url, jsonContent);
                    }
                    else
                    {
                        return false;
                    }
                }

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("EIN success notification sent to Salesforce");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to notify success: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NotifySalesforceSuccessAsync");
                return false;
            }
        }

        public async Task<bool> NotifySalesforceErrorCodeAsync(string? entityProcessId, string? errorCode, string? status = "fail")
        {
            if (string.IsNullOrEmpty(entityProcessId))
            {
                _logger.LogError("Invalid input parameter for NotifySalesforceErrorCodeAsync: entityProcessId is null or empty");
                return false;
            }

            try
            {
                _logger.LogInformation("Notifying error for entity process ID: {EntityProcessId}, Error: {ErrorCode}", 
                    entityProcessId, errorCode);

                if (!await EnsureAuthenticatedAsync())
                {
                    _logger.LogError("Failed to authenticate with Salesforce");
                    return false;
                }

                var baseUrl = "https://corpnet--fullphase2.sandbox.my.salesforce.com";
                var url = $"{baseUrl}/services/apexrest/service/v2/formautomation/ein/update?entityProcessId={entityProcessId}";

                var payload = new
                {
                    entityProcessId,
                    einNumber = "",
                    errorCode = errorCode ?? "",
                    status = status ?? "fail"
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.PostAsync(url, jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("EIN error notification sent: {ErrorCode}", errorCode);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var parsedError = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(errorContent);
                        if (parsedError != null)
                        {
                            foreach (var err in parsedError)
                            {
                                if (err != null && err.ContainsKey("errorCode") && err.ContainsKey("message"))
                                {
                                    _logger.LogWarning("Salesforce Error - Code: {Code}, Message: {Message}", 
                                        err["errorCode"], err["message"]);
                                }
                            }
                        }
                    }
                    catch
                    {
                        _logger.LogWarning("Could not parse detailed Salesforce error");
                    }
                    _logger.LogError("Failed to notify error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NotifySalesforceErrorCodeAsync");
                return false;
            }
        }

        public async Task<bool> NotifyScreenshotUploadToSalesforceAsync(string? entityProcessId, string? blobUrl, string? entityName, string? docType)
        {
            if (string.IsNullOrEmpty(entityProcessId) || string.IsNullOrEmpty(blobUrl) || string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(docType))
            {
                _logger.LogError("Invalid input parameters for NotifyScreenshotUploadToSalesforceAsync: entityProcessId, blobUrl, entityName, or docType is null or empty");
                return false;
            }

            try
            {
                _logger.LogInformation("Notifying PDF upload for entity process ID: {EntityProcessId}, Document Type: {DocType}", 
                    entityProcessId, docType);

                if (!await EnsureAuthenticatedAsync())
                {
                    _logger.LogError("Failed to authenticate with Salesforce");
                    return false;
                }

                var cleanName = Regex.Replace(entityName, @"\s+", "");
                var fileSuffix = docType == "letter" ? "EINLetter" : "EINConfirmation";
                var fileName = $"{cleanName}-ID-{fileSuffix}";
                var extension = "pdf";
                var migrationId = $"{blobUrl.GetHashCode()}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                var payload = new
                {
                    Name = $"{fileName}.pdf",
                    File_Extension__c = extension,
                    Migration_ID__c = migrationId,
                    File_Name__c = fileName,
                    Parent_Name__c = "EntityProcess",
                    Entity_Process_Id__c = entityProcessId,
                    Blob_URL__c = blobUrl,
                    Is_Content_Created__c = false,
                    Is_Errored__c = false,
                    Historical_Record__c = false,
                    Exclude_from_Partner_API__c = false,
                    Deleted_by_Client__c = false,
                    Hidden_From_Client__c = (docType != "letter")
                };

                var apiVersion = docType == "letter" ? "v63.0" : "v59.0";
                var url = $"{_instanceUrl}/services/data/{apiVersion}/sobjects/Content_Migration__c/";

                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.PostAsync(url, jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("{FileSuffix} upload notification sent successfully", fileSuffix);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to notify upload ({DocType}): {StatusCode} - {Error}", 
                        docType, response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NotifyScreenshotUploadToSalesforceAsync");
                return false;
            }
        }

        public async Task<bool> NotifyEinLetterToSalesforceAsync(string? entityProcessId, string? blobUrl, string? entityName)
        {
            return await NotifyScreenshotUploadToSalesforceAsync(entityProcessId, blobUrl, entityName, "letter");
        }

        public async Task<bool> NotifyConfirmationAsync(string? entityProcessId, string? blobUrl, string? entityName)
        {
            return await NotifyScreenshotUploadToSalesforceAsync(entityProcessId, blobUrl, entityName, "confirmation");
        }

        private class SalesforceTokenResponse
        {
            public string? access_token { get; set; }
            public string? instance_url { get; set; }
            public string? token_type { get; set; }
        }

        public async Task NotifyContentMigrationAsync(string? entityProcessId, string? blobUrl, string? fileName, string? extension, bool hiddenFromClient, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(entityProcessId) || string.IsNullOrEmpty(blobUrl) || string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(extension))
            {
                _logger.LogWarning("Invalid input parameters for NotifyContentMigrationAsync: entityProcessId, blobUrl, fileName, or extension is null or empty");
                return;
            }

            if (!await EnsureAuthenticatedAsync())
            {
                _logger.LogError("Failed to authenticate with Salesforce");
                return;
            }

            var payload = new
            {
                Name = $"{fileName}.{extension}",
                File_Extension__c = extension,
                Migration_ID__c = $"{blobUrl.GetHashCode()}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                File_Name__c = fileName,
                Parent_Name__c = "EntityProcess",
                Entity_Process_Id__c = entityProcessId,
                Blob_URL__c = blobUrl,
                Is_Content_Created__c = false,
                Is_Errored__c = false,
                Historical_Record__c = false,
                Exclude_from_Partner_API__c = false,
                Deleted_by_Client__c = false,
                Hidden_From_Client__c = hiddenFromClient
            };

            var url = $"{_instanceUrl}/services/data/v59.0/sobjects/Content_Migration__c/";
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to notify content migration: {StatusCode} - {Error}", response.StatusCode, error);
                return;
            }
            else
            {
                _logger.LogInformation("Salesforce content migration recorded for {FileName}", fileName);
                return;
            }
        }

        public async Task UpdateEinStatusAsync(string? entityProcessId, string? status, string? einNumber, string? errorCode, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(entityProcessId) || string.IsNullOrEmpty(status))
            {
                _logger.LogError("Invalid input parameters for UpdateEinStatusAsync: entityProcessId or status is null or empty");
                return;
            }

            if (!await EnsureAuthenticatedAsync())
            {
                _logger.LogError("Failed to authenticate with Salesforce");
                return;
            }

            var url = $"{_instanceUrl}/services/apexrest/service/v2/formautomation/ein/update?entityProcessId={entityProcessId}";
            var payload = new
            {
                entityProcessId,
                einNumber = einNumber ?? "",
                errorCode = errorCode ?? "",
                status
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.PostAsync(url, jsonContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to update EIN status: {StatusCode} - {Error}", response.StatusCode, error);
            }
            else
            {
                _logger.LogInformation("Updated EIN status for entity process ID: {EntityProcessId}", entityProcessId);
            }

            return;
        }
    }
}