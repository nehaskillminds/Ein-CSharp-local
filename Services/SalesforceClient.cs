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
using OpenQA.Selenium;


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

        private readonly IErrorMessageExtractionService _errorMessageExtractionService;
        
        public async Task<bool> NotifySalesforceErrorCodeAsync(string? entityProcessId, string? errorCode, string? status = "fail", IWebDriver? driver = null, string? htmlContent = null)
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

                    // Extract error message using the new service
                    string errorMessage = string.Empty;
                    
                    if (driver != null)
                    {
                        errorMessage = _errorMessageExtractionService.ExtractErrorMessage(driver);
                    }
                    else if (!string.IsNullOrEmpty(htmlContent))
                    {
                        errorMessage = _errorMessageExtractionService.ExtractErrorMessage(htmlContent);
                    }

                    var baseUrl = "https://corpnet--fullphase2.sandbox.my.salesforce.com";
                    var url = $"{baseUrl}/services/apexrest/service/v2/formautomation/ein/update?entityProcessId={entityProcessId}";

                    var payload = new
                    {
                        entityProcessId,
                        einNumber = "",
                        message = errorMessage, // Now using the extracted error message
                        errorCode = errorCode ?? "",
                        status = status ?? "fail"
                    };

                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

                    var response = await _httpClient.PostAsync(url, jsonContent);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("EIN error notification sent: {ErrorCode}, Message: {ErrorMessage}", errorCode, errorMessage);
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
    }}