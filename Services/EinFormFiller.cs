using Microsoft.Extensions.Logging;
using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Interactions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using SeleniumExtras.WaitHelpers;
using System.Text.RegularExpressions;


namespace EinAutomation.Api.Services
{
    public abstract class EinFormFiller : IEinFormFiller
    {
        protected IWebDriver? Driver { get; private set; }
        protected WebDriverWait? Wait { get; set; }
        protected readonly ILogger<EinFormFiller> _logger;
        protected readonly IBlobStorageService _blobStorageService;
        protected int Timeout { get; }
        protected bool Headless { get; }
        protected string? DriverLogPath { get; } = Path.Combine(Path.GetTempPath(), "chromedriver.log");
        protected List<Dictionary<string, object?>> ConsoleLogs { get; } = new List<Dictionary<string, object?>>();
        protected bool ConfirmationUploaded { get; set; } = false;

        public EinFormFiller(
            ILogger<EinFormFiller> logger,
            IBlobStorageService blobStorageService,
            bool headless = false,
            int timeout = 300)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
            Headless = headless;
            Timeout = timeout;
        }

        public bool FillField(By locator, string? value, string label = "field")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning("Skipping {Label} - empty value", label);
                return false;
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    _logger.LogWarning("Cannot fill {Label} - Wait or Driver is null", label);
                    return false;
                }

                var field = Wait.Until(ExpectedConditions.ElementToBeClickable(locator));
                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", field);
                field.Clear();
                field.SendKeys(value);
                _logger.LogInformation("Filled {Label}: {Value}", label, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fill {Label}", label);
                return false;
            }
        }

        public bool ClickButton(By locator, string description = "button", int retries = 3)
        {
            if (Wait == null || Driver == null)
            {
                _logger.LogWarning("Cannot click {Description} - Wait or Driver is null", description);
                return false;
            }

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    var element = Wait.Until(ExpectedConditions.ElementExists(locator));
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    Task.Delay(500).Wait();

                    var clickableElement = Wait.Until(ExpectedConditions.ElementToBeClickable(locator));
                    
                    try
                    {
                        clickableElement.Click();
                        _logger.LogInformation("Clicked {Description}", description);
                        Task.Delay(1000).Wait();
                        return true;
                    }
                    catch
                    {
                        try
                        {
                            ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].click();", clickableElement);
                            _logger.LogInformation("Clicked {Description} via JavaScript", description);
                            Task.Delay(1000).Wait();
                            return true;
                        }
                        catch
                        {
                            new Actions(Driver).MoveToElement(clickableElement).Click().Perform();
                            _logger.LogInformation("Clicked {Description} via Actions", description);
                            Task.Delay(1000).Wait();
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == retries)
                    {
                        _logger.LogWarning(ex, "Failed to click {Description} after {Retries} attempts", description, retries + 1);
                        return false;
                    }
                    _logger.LogWarning(ex, "Click attempt {Attempt} failed for {Description}, retrying...", attempt + 1, description);
                    Task.Delay(1000).Wait();
                }
            }
            return false;
        }

        public bool SelectRadio(string? radioId, string description = "radio")
        {
            if (string.IsNullOrEmpty(radioId))
            {
                _logger.LogWarning("Cannot select {Description} - radioId is null or empty", description);
                return false;
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    _logger.LogWarning("Cannot select {Description} - Wait or Driver is null", description);
                    return false;
                }

                var radio = Wait.Until(ExpectedConditions.ElementToBeClickable(By.Id(radioId)));
                ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", radio);
                
                var result = ((IJavaScriptExecutor)Driver).ExecuteScript(
                    $"document.getElementById('{radioId}').checked = true; return document.getElementById('{radioId}').checked;");
                
                if (result != null && (bool)result)
                {
                    _logger.LogInformation("Selected {Description} via JavaScript", description);
                    return true;
                }
                
                radio.Click();
                _logger.LogInformation("Selected {Description} via click", description);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to select {Description} (ID: {RadioId})", description, radioId);
                return false;
            }
        }

        public bool SelectDropdown(By locator, string? value, string label = "dropdown")
        {
            if (string.IsNullOrEmpty(value))
            {
                _logger.LogWarning("Cannot select {Label} - value is null or empty", label);
                return false;
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    _logger.LogWarning("Cannot select {Label} - Wait or Driver is null", label);
                    return false;
                }

                var element = Wait.Until(ExpectedConditions.ElementToBeClickable(locator));
                var select = new SelectElement(element);
                select.SelectByValue(value);
                _logger.LogInformation("Selected {Label}: {Value}", label, value);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to select {Label}", label);
                return false;
            }
        }

        public void Cleanup()
        {
            try
            {
                CaptureBrowserLogs();
                if (Driver != null)
                {
                    Driver.Quit();
                    Driver = null;
                    _logger.LogInformation("Browser closed successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing browser");
                try
                {
                    if (Driver is ChromeDriver chromeDriver)
                    {
                        chromeDriver.Dispose();
                    }
                    _logger.LogWarning("Force-disposed browser");
                }
                catch (Exception disposeEx)
                {
                    _logger.LogError(disposeEx, "Failed to force-dispose browser");
                }
            }
            finally
            {
                if (File.Exists(DriverLogPath))
                {
                    try
                    {
                        File.Delete(DriverLogPath);
                        _logger.LogDebug("Removed ChromeDriver log file: {DriverLogPath}", DriverLogPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove ChromeDriver log");
                    }
                }
            }
        }

        public void ClearAndFill(By locator, string? value, string description)
        {
            if (string.IsNullOrEmpty(value))
            {
                _logger.LogError("Cannot clear and fill {Description} - value is null or empty", description);
                throw new AutomationError($"Cannot clear and fill {description} - value is null or empty", "Value is null");
            }

            try
            {
                if (Wait == null || Driver == null)
                {
                    throw new InvalidOperationException("Wait or Driver is null");
                }

                var field = Wait.Until(ExpectedConditions.ElementToBeClickable(locator));
                field.Clear();
                field.SendKeys(value);
                _logger.LogInformation("Cleared and filled {Description} with value: {Value}", description, value);
            }
            catch (Exception ex)
            {
                CaptureBrowserLogs();
                _logger.LogError(ex, "Failed to clear and fill {Description}", description);
                throw new AutomationError($"Failed to clear and fill {description}", ex.Message);
            }
        }

// Simple drop-in replacement for your existing method
        public async Task<(string? BlobUrl, bool Success)> CapturePageAsPdf(CaseData? data, CancellationToken cancellationToken)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot capture PDF - Driver is null");
                    return (null, false);
                }

                // Generate a clean filename
                var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINConfirmation.pdf";

                // Wait for page to be fully loaded
                await WaitForPageLoadAsync(cancellationToken);

                // Try Chrome CDP method first (most reliable)
                if (Driver is ChromeDriver chromeDriver)
                {
                    try
                    {
                        var printOptions = new Dictionary<string, object>
                        {
                            {"landscape", false},
                            {"displayHeaderFooter", false},
                            {"printBackground", true},
                            {"preferCSSPageSize", true},
                            {"paperWidth", 8.27}, // A4 width in inches
                            {"paperHeight", 11.69}, // A4 height in inches
                            {"marginTop", 0.39},
                            {"marginBottom", 0.39},
                            {"marginLeft", 0.39},
                            {"marginRight", 0.39}
                        };

                        var result = chromeDriver.ExecuteCdpCommand("Page.printToPDF", printOptions);
                        
                        
                        // Safe casting and null checking
                        if (result is Dictionary<string, object> resultDict && resultDict.ContainsKey("data"))
                        {
                            var pdfData = resultDict["data"]?.ToString();
                            if (!string.IsNullOrEmpty(pdfData))
                            {
                                var pdfBytes = Convert.FromBase64String(pdfData);
                                var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                                _logger.LogInformation($"PDF successfully uploaded to: {blobUrl}");
                                return (blobUrl, true);
                            }
                        }
                    }
                    catch (Exception cdpEx)
                    {
                        _logger.LogWarning($"Chrome CDP PDF generation failed, trying fallback: {cdpEx.Message}");
                    }
                }

                // Fallback to html2pdf.js method
                return await CapturePageAsPdfHtml2PdfFallback(data, blobName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in CapturePageAsPdf: {ex.Message}");
                return (null, false);
            }
        }

        private async Task<(string? BlobUrl, bool Success)> CapturePageAsPdfHtml2PdfFallback(CaseData? data, string blobName, CancellationToken cancellationToken)
        {
            try
            {
                // Set script timeout
                Driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);

                string html2pdfScript = @"
                    var callback = arguments[arguments.length - 1];
                    
                    function loadHtml2Pdf() {
                        if (window.html2pdf) {
                            generatePdf();
                            return;
                        }
                        
                        var script = document.createElement('script');
                        script.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2pdf.js/0.10.1/html2pdf.bundle.min.js';
                        script.onload = function() {
                            setTimeout(generatePdf, 500);
                        };
                        script.onerror = function() {
                            callback(JSON.stringify({ success: false, error: 'Failed to load html2pdf.js' }));
                        };
                        document.head.appendChild(script);
                    }
                    
                    function generatePdf() {
                        try {
                            var element = document.body;
                            var opt = {
                                margin: 10,
                                filename: 'document.pdf',
                                image: { type: 'jpeg', quality: 0.98 },
                                html2canvas: { 
                                    scale: 1,
                                    logging: false,
                                    useCORS: true,
                                    allowTaint: true,
                                    letterRendering: true
                                },
                                jsPDF: { 
                                    unit: 'mm', 
                                    format: 'a4', 
                                    orientation: 'portrait' 
                                }
                            };
                            
                            html2pdf()
                                .set(opt)
                                .from(element)
                                .toPdf()
                                .get('pdf')
                                .then(function(pdf) {
                                    var pdfBlob = pdf.output('blob');
                                    var reader = new FileReader();
                                    reader.onloadend = function() {
                                        var base64data = reader.result.split(',')[1];
                                        callback(JSON.stringify({ success: true, data: base64data }));
                                    };
                                    reader.onerror = function() {
                                        callback(JSON.stringify({ success: false, error: 'Failed to convert to base64' }));
                                    };
                                    reader.readAsDataURL(pdfBlob);
                                })
                                .catch(function(error) {
                                    callback(JSON.stringify({ success: false, error: error.toString() }));
                                });
                        } catch (error) {
                            callback(JSON.stringify({ success: false, error: error.toString() }));
                        }
                    }
                    
                    loadHtml2Pdf();
                ";

                // Execute the script with proper error handling
                var result = ((IJavaScriptExecutor)Driver).ExecuteAsyncScript(html2pdfScript);
                var resultJson = result?.ToString();
                
                if (!string.IsNullOrEmpty(resultJson))
                {
                    try
                    {
                        var resultData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
                        
                        if (resultData != null && resultData.ContainsKey("success"))
                        {
                            var successElement = resultData["success"];
                            bool success = false;
                            
                            // Handle different JSON element types
                            if (successElement is System.Text.Json.JsonElement jsonElement)
                            {
                                success = jsonElement.GetBoolean();
                            }
                            else if (successElement is bool boolValue)
                            {
                                success = boolValue;
                            }
                            
                            if (success && resultData.ContainsKey("data"))
                            {
                                var dataElement = resultData["data"];
                                string? base64Pdf = null;
                                
                                if (dataElement is System.Text.Json.JsonElement jsonDataElement)
                                {
                                    base64Pdf = jsonDataElement.GetString();
                                }
                                else if (dataElement is string stringValue)
                                {
                                    base64Pdf = stringValue;
                                }
                                
                                if (!string.IsNullOrEmpty(base64Pdf))
                                {
                                    var pdfBytes = Convert.FromBase64String(base64Pdf);
                                    var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                                    _logger.LogInformation($"PDF successfully uploaded to: {blobUrl}");
                                    return (blobUrl, true);
                                }
                            }
                            else
                            {
                                var errorElement = resultData.ContainsKey("error") ? resultData["error"] : null;
                                string? error = null;
                                
                                if (errorElement is System.Text.Json.JsonElement jsonErrorElement)
                                {
                                    error = jsonErrorElement.GetString();
                                }
                                else if (errorElement is string stringErrorValue)
                                {
                                    error = stringErrorValue;
                                }
                                
                                _logger.LogError($"PDF generation failed: {error ?? "Unknown error"}");
                            }
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        _logger.LogError($"Failed to parse JSON result: {jsonEx.Message}");
                    }
                }

                _logger.LogError("PDF generation failed - no valid result");
                return (null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in HTML2PDF fallback: {ex.Message}");
                return (null, false);
            }
        }

        private async Task WaitForPageLoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(10));
                await Task.Run(() =>
                {
                    wait.Until(driver => ((IJavaScriptExecutor)driver).ExecuteScript("return document.readyState").Equals("complete"));
                }, cancellationToken);

                // Additional wait for dynamic content
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Page load wait failed: {ex.Message}");
            }
        }

        public void CaptureBrowserLogs()
        {
            try
            {
                if (Driver != null)
                {
                    var logs = Driver.Manage().Logs.GetLog(LogType.Browser);
                    ConsoleLogs.AddRange(logs.Select(log => new Dictionary<string, object?>
                    {
                        {"level", log.Level.ToString()},
                        {"message", log.Message}
                    }));
                    
                    foreach (var log in logs)
                    {
                        _logger.LogDebug("Browser console: {Level} - {Message}", log.Level, log.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture browser logs");
            }
        }

        public void LogSystemResources(string? referenceId = null, CancellationToken cancellationToken = default)
        {
            var currentProcess = Process.GetCurrentProcess();
            var memoryUsed = currentProcess.WorkingSet64;
            var cpuTime = currentProcess.TotalProcessorTime;

            _logger.LogInformation("System Resources - Ref: {ReferenceId}, Memory: {MemoryUsed} bytes, CPU Time: {CpuTime}",
                referenceId ?? "N/A", memoryUsed, cpuTime);
        }

        public abstract Task NavigateAndFillForm(CaseData? data, Dictionary<string, object?>? jsonData);
        public abstract Task HandleTrusteeshipEntity(CaseData? data);
        public abstract Task<(bool Success, string? Message, string? AzureBlobUrl)> RunAutomation(CaseData? data);

        public string NormalizeState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                _logger.LogError("State cannot be empty");
                throw new ArgumentException("State cannot be empty", nameof(state));
            }

            var stateClean = state.ToUpper().Trim();
            
            var stateMapping = new Dictionary<string, string>
            {
                {"ALABAMA", "AL"}, {"ALASKA", "AK"}, {"ARIZONA", "AZ"}, {"ARKANSAS", "AR"}, 
                {"CALIFORNIA", "CA"}, {"COLORADO", "CO"}, {"CONNECTICUT", "CT"}, {"DELAWARE", "DE"},
                {"FLORIDA", "FL"}, {"GEORGIA", "GA"}, {"HAWAII", "HI"}, {"IDAHO", "ID"},
                {"ILLINOIS", "IL"}, {"INDIANA", "IN"}, {"IOWA", "IA"}, {"KANSAS", "KS"},
                {"KENTUCKY", "KY"}, {"LOUISIANA", "LA"}, {"MAINE", "ME"}, {"MARYLAND", "MD"},
                {"MASSACHUSETTS", "MA"}, {"MICHIGAN", "MI"}, {"MINNESOTA", "MN"}, {"MISSISSIPPI", "MS"},
                {"MISSOURI", "MO"}, {"MONTANA", "MT"}, {"NEBRASKA", "NE"}, {"NEVADA", "NV"},
                {"NEW HAMPSHIRE", "NH"}, {"NEW JERSEY", "NJ"}, {"NEW MEXICO", "NM"}, {"NEW YORK", "NY"},
                {"NORTH CAROLINA", "NC"}, {"NORTH DAKOTA", "ND"}, {"OHIO", "OH"}, {"OKLAHOMA", "OK"},
                {"OREGON", "OR"}, {"PENNSYLVANIA", "PA"}, {"RHODE ISLAND", "RI"}, {"SOUTH CAROLINA", "SC"},
                {"SOUTH DAKOTA", "SD"}, {"TENNESSEE", "TN"}, {"TEXAS", "TX"}, {"UTAH", "UT"},
                {"VERMONT", "VT"}, {"VIRGINIA", "VA"}, {"WASHINGTON", "WA"}, {"WEST VIRGINIA", "WV"},
                {"WISCONSIN", "WI"}, {"WYOMING", "WY"}, {"DISTRICT OF COLUMBIA", "DC"}
            };

            if (stateClean.Length == 2 && stateMapping.Values.Contains(stateClean))
                return stateClean;

            if (stateMapping.ContainsKey(stateClean))
                return stateMapping[stateClean];

            foreach (var kvp in stateMapping)
            {
                if (stateClean == kvp.Key.ToUpper())
                    return kvp.Value;
            }

            var reverseMapping = stateMapping.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            if (reverseMapping.ContainsKey(stateClean))
                return stateClean;

            return stateClean;
        }

        public (string? Month, int Year) ParseFormationDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                _logger.LogWarning("Invalid date format: null or empty, using default date");
                return (null, 0);
            }

            var formats = new[] { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd", "MM/dd/yyyy", "yyyy/MM/dd" };
            
            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(dateStr.Trim(), fmt, null, System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    return (parsed.Month.ToString(), parsed.Year);
                }
            }

            _logger.LogWarning("Invalid date format: {DateStr}, using default date", dateStr);
            return (null, 0);
        }

        public Dictionary<string, object?> GetDefaults(CaseData? data)
        {
            if (data == null)
            {
                _logger.LogError("Cannot get         defaults - CaseData is null");
                return new Dictionary<string, object?>();
            }

            var rawMembers = data.EntityMembers ?? new Dictionary<string, string>();
            
            var entityMembersDict = new Dictionary<string, string?>
            {
                {"first_name_1", (rawMembers.GetValueOrDefault("first_name_1") ?? "").Trim()},
                {"last_name_1", (rawMembers.GetValueOrDefault("last_name_1") ?? "").Trim()},
                {"middle_name_1", (rawMembers.GetValueOrDefault("middle_name_1") ?? "").Trim()},
                {"phone_1", (rawMembers.GetValueOrDefault("phone_1") ?? "").Trim()}
            };

            var mailingAddress = data.MailingAddress ?? new Dictionary<string, string>();
            
            return new Dictionary<string, object?>
            {
                {"first_name", entityMembersDict["first_name_1"]},
                {"last_name", entityMembersDict["last_name_1"]},
                {"middle_name", entityMembersDict["middle_name_1"]},
                {"phone", entityMembersDict["phone_1"]},
                {"ssn_decrypted", data.SsnDecrypted ?? ""},
                {"entity_name", data.EntityName ?? ""},
                {"business_address_1", data.BusinessAddress1 ?? ""},
                {"city", data.City ?? ""},
                {"zip_code", data.ZipCode ?? ""},
                {"business_description", data.BusinessDescription ?? "Any and lawful business"},
                {"formation_date", data.FormationDate ?? ""},
                {"county", data.County ?? ""},
                {"trade_name", data.TradeName ?? ""},
                {"care_of_name", data.CareOfName ?? ""},
                {"mailing_address", mailingAddress},
                {"closing_month", data.ClosingMonth ?? ""},
                {"filing_requirement", data.FilingRequirement ?? ""}
            };
        }

        public void InitializeDriver()
            {
                try
                {
                    LogSystemResources(); // You need to implement this
                    
                    var options = new ChromeOptions();
                    // Set Chrome arguments
                    options.AddArgument("--disable-gpu");
                    options.AddArgument("--enable-unsafe-swiftshader");
                    options.AddArgument("--no-sandbox");
                    options.AddArgument("--disable-dev-shm-usage");
                    options.AddArgument("--disable-blink-features=AutomationControlled");
                    options.AddArgument("--disable-infobars");
                    options.AddArgument("--window-size=1920,1080");
                    options.AddArgument("--start-maximized");
        

                
                    
                    // Set Chrome preferences
                    var prefs = new Dictionary<string, object>
                    {
                        ["profile.default_content_setting_values.popups"] = 2,
                        ["profile.default_content_setting_values.notifications"] = 2,
                        ["profile.default_content_setting_values.geolocation"] = 2,
                        ["credentials_enable_service"] = false,
                        ["profile.password_manager_enabled"] = false,
                        ["autofill.profile_enabled"] = false,
                        ["autofill.credit_card_enabled"] = false,
                        ["password_manager_enabled"] = false,
                        ["profile.password_dismissed_save_prompt"] = true
                    };
                    options.AddUserProfilePreference("prefs", prefs);
                    
                    // Option 1: Specify ChromeDriver path explicitly
                    var service = ChromeDriverService.CreateDefaultService(@"C:\Users\skill\OneDrive\Desktop\EinAutomationRevisedLocal2.Api\EinAutomation.Api\chromedriver.exe");
                    Driver = new ChromeDriver(service, options);
                    
                    // Option 2: Or use default if ChromeDriver is in PATH
                    // Driver = new ChromeDriver(options);
                    
                    Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(Timeout));
                    
                    // Override JS functions
                    var js = (IJavaScriptExecutor)Driver;
                    js.ExecuteScript(@"
                        window.alert = function() { return true; };
                        window.confirm = function() { return true; };
                        window.prompt = function() { return null; };
                        window.open = function() { return null; };
                    ");
                    
                    Console.WriteLine("- WebDriver initialized successfully");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to initialize WebDriver: {ex.Message}");
                    throw;
                }
            }

        public virtual async Task<(bool Success, string? EinNumber, byte[]? ConfirmationPdfBytes, string? BlobName)> FillAsync(CaseData? data, CancellationToken ct)
        {
            if (data == null)
            {
                _logger.LogError("Cannot fill form - CaseData is null");
                return (false, null, null, null);
            }

            try
            {
                await NavigateAndFillForm(data, GetDefaults(data));
                
                var (blobUrl, success) = await CapturePageAsPdf(data, ct);
                if (!success || string.IsNullOrWhiteSpace(blobUrl))
                    return (false, null, null, null);

                byte[] pdfBytes = await DownloadBlobAsync(blobUrl, ct);
                string blobName = Path.GetFileName(blobUrl);
                string? einNumber = "PLACEHOLDER_EIN"; // Replace this with actual EIN extraction logic

                return (true, einNumber, pdfBytes, blobName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FillAsync failed");
                return (false, null, null, null);
            }
        }

        public virtual async Task<(bool Success, string? EinNumber, byte[]? EinLetterPdfBytes, string? BlobName)> FinalSubmitAsync(CaseData? data, CancellationToken ct)
        {
            if (data == null)
            {
                _logger.LogError("Cannot submit form - CaseData is null");
                return (false, null, null, null);
            }

            try
            {
                var (blobUrl, success) = await CapturePageAsPdf(data, ct);
                if (!success || string.IsNullOrWhiteSpace(blobUrl))
                    return (false, null, null, null);

                byte[] pdfBytes = await DownloadBlobAsync(blobUrl, ct);
                string blobName = Path.GetFileName(blobUrl);
                string? einNumber = "PLACEHOLDER_FINAL_EIN"; // Replace this with actual EIN extraction logic

                return (true, einNumber, pdfBytes, blobName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FinalSubmitAsync failed");
                return (false, null, null, null);
            }
        }

        public virtual Task<byte[]?> CaptureFailurePageAsync(CaseData? data, CancellationToken ct)
        {
            if (data == null || Driver == null)
            {
                _logger.LogError("Cannot capture failure page - CaseData or Driver is null");
                return Task.FromResult<byte[]?>(null);
            }

            try
            {
                Screenshot screenshot = ((ITakesScreenshot)Driver).GetScreenshot();
                return Task.FromResult<byte[]?>(screenshot.AsByteArray);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptureFailurePageAsync failed");
                return Task.FromResult<byte[]?>(null);
            }
        }

        public virtual Task<byte[]?> GetBrowserLogsAsync(CaseData? data, CancellationToken ct)
        {
            if (data == null || Driver == null)
            {
                _logger.LogError("Cannot get browser logs - CaseData or Driver is null");
                return Task.FromResult<byte[]?>(null);
            }

            try
            {
                var logs = Driver.Manage().Logs.GetLog(LogType.Browser);
                if (logs == null)
                    return Task.FromResult<byte[]?>(null);

                var formatted = string.Join("\n", logs.Select(log => $"{log.Timestamp} [{log.Level}] {log.Message}"));
                return Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(formatted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBrowserLogsAsync failed");
                return Task.FromResult<byte[]?>(null);
            }
        }

        public virtual async Task CleanupAsync(CancellationToken ct)
        {
            Cleanup();
            await Task.CompletedTask;
        }

        protected async Task<byte[]> DownloadBlobAsync(string? blobUrl, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(blobUrl))
            {
                _logger.LogError("Cannot download blob - blobUrl is null or empty");
                throw new ArgumentException("Blob URL cannot be null or empty", nameof(blobUrl));
            }

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(blobUrl, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }
}