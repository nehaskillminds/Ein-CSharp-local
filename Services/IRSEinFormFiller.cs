using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using HtmlAgilityPack;
using EinAutomation.Api.Infrastructure;

namespace EinAutomation.Api.Services
{
    public class IRSEinFormFiller : EinFormFiller
    {
        private readonly HttpClient _httpClient;
        private readonly IErrorMessageExtractionService _errorMessageExtractionService;

        public static readonly Dictionary<string, string> EntityTypeMapping = new Dictionary<string, string>
        {
            { "Sole Proprietorship", "Sole Proprietor" },
            { "Individual", "Sole Proprietor" },
            { "Partnership", "Partnership" },
            { "Joint venture", "Partnership" },
            { "Limited Partnership", "Partnership" },
            { "General partnership", "Partnership" },
            { "C-Corporation", "Corporations" },
            { "S-Corporation", "Corporations" },
            { "Professional Corporation", "Corporations" },
            { "Corporation", "Corporations" },
            { "Non-Profit Corporation", "View Additional Types, Including Tax-Exempt and Governmental Organizations" },
            { "Limited Liability", "Limited Liability Company (LLC)" },
            { "Company (LLC)", "Limited Liability Company (LLC)" },
            { "LLC", "Limited Liability Company (LLC)" },
            { "Limited Liability Company", "Limited Liability Company (LLC)" },
            { "Limited Liability Company (LLC)", "Limited Liability Company (LLC)" },
            { "Professional Limited Liability Company", "Limited Liability Company (LLC)" },
            { "Limited Liability Partnership", "Partnership" },
            { "LLP", "Partnership" },
            { "Professional Limited Liability Company (PLLC)", "Limited Liability Company (LLC)" },
            { "Association", "View Additional Types, Including Tax-Exempt and Governmental Organizations" },
            { "Co-ownership", "Partnership" },
            { "Doing Business As (DBA)", "Sole Proprietor" },
            { "Trusteeship", "Trusts" }
        };

        public static readonly Dictionary<string, string> RadioButtonMapping = new Dictionary<string, string>
        {
            { "Sole Proprietor", "sole" },
            { "Partnership", "partnerships" },
            { "Corporations", "corporations" },
            { "Limited Liability Company (LLC)", "limited" },
            { "Estate", "estate" },
            { "Trusts", "trusts" },
            { "View Additional Types, Including Tax-Exempt and Governmental Organizations", "viewadditional" }
        };

        public static readonly Dictionary<string, string> SubTypeMapping = new Dictionary<string, string>
        {
            { "Sole Proprietorship", "Sole Proprietor" },
            { "Individual", "Sole Proprietor" },
            { "Partnership", "Partnership" },
            { "Joint venture", "Joint Venture" },
            { "Limited Partnership", "Partnership" },
            { "General partnership", "Partnership" },
            { "C-Corporation", "Corporation" },
            { "S-Corporation", "S Corporation" },
            { "Professional Corporation", "Personal Service Corporation" },
            { "Corporation", "Corporation" },
            { "Non-Profit Corporation", "**This is dependent on the business_description**" },
            { "Limited Liability", "N/A" },
            { "Limited Liability Company (LLC)", "N/A" },
            { "LLC", "N/A" },
            { "Limited Liability Company", "N/A" },
            { "Professional Limited Liability Company", "N/A" },
            { "Limited Liability Partnership", "Partnership" },
            { "LLP", "Partnership" },
            { "Professional Limited Liability Company (PLLC)", "N/A" },
            { "Association", "N/A" },
            { "Co-ownership", "Partnership" },
            { "Doing Business As (DBA)", "N/A" },
            { "Trusteeship", "Irrevocable Trust" }
        };

        public static readonly Dictionary<string, string> SubTypeButtonMapping = new Dictionary<string, string>
        {
            { "Sole Proprietor", "sole" },
            { "Household Employer", "house" },
            { "Partnership", "parnership" },
            { "Joint Venture", "joint" },
            { "Corporation", "corp" },
            { "S Corporation", "scorp" },
            { "Personal Service Corporation", "personalservice" },
            { "Irrevocable Trust", "irrevocable" },
            { "Non-Profit/Tax-Exempt Organization", "nonprofit" },
            { "Other", "other_option" }
        };

        public IRSEinFormFiller(
            ILogger<IRSEinFormFiller>? logger,
            IBlobStorageService? blobStorageService,
            ISalesforceClient? salesforceClient,
            HttpClient? httpClient,
            IErrorMessageExtractionService errorMessageExtractionService)
            : base(logger ?? throw new ArgumentNullException(nameof(logger)), 
                   blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService)),
                   salesforceClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _errorMessageExtractionService = errorMessageExtractionService ?? throw new ArgumentNullException(nameof(errorMessageExtractionService));
        }

        private async Task<bool> DetectAndHandleType2Failure(CaseData? data, Dictionary<string, object?>? jsonData)
        {
            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Cannot detect Type 2 failure - Driver is null");
                    return false;
                }

                var pageText = Driver?.PageSource?.ToLower() ?? string.Empty;
                if (pageText.Contains("we are unable to provide you with an ein"))
                {
                    string? referenceNumber = null;

                    // Primary attempt: Regex
                    var refMatch = Regex.Match(pageText, @"reference number\s+(\d+)");
                    if (refMatch.Success)
                    {
                        referenceNumber = refMatch.Groups[1].Value;
                        _logger.LogInformation("Extracted IRS Reference Number: {ReferenceNumber}", referenceNumber);
                    }
                    else
                    {
                        _logger.LogWarning("Primary reference number extraction failed. Attempting fallback with HtmlAgilityPack.");

                        try
                        {
                            var doc = new HtmlDocument();
                            doc.LoadHtml(Driver.PageSource);

                            var textNodes = doc.DocumentNode.SelectNodes("//text()[contains(., 'reference number')]");
                            if (textNodes != null)
                            {
                                foreach (var node in textNodes)
                                {
                                    var match = Regex.Match(node.InnerText, @"reference number\s+(\d+)");
                                    if (match.Success)
                                    {
                                        referenceNumber = match.Groups[1].Value;
                                        _logger.LogInformation("Extracted IRS Reference Number via fallback: {ReferenceNumber}", referenceNumber);
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Fallback parsing of reference number failed: {Message}", ex.Message);
                        }
                    }

                    if (!string.IsNullOrEmpty(referenceNumber))
                    {
                        if (jsonData != null && Driver != null)
                        {
                            var errorMsg = _errorMessageExtractionService.ExtractErrorMessage(Driver);
                            jsonData["irs_reference_number"] = referenceNumber;
                            jsonData["error_message"] = errorMsg;
                            await _blobStorageService.SaveJsonDataSync(jsonData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? new object()));
                        }
                        var (blobUrl, success) = await CaptureFailurePageAsPdf(data, CancellationToken.None);
                        if (success && !string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId, referenceNumber, "fail", Driver);
                        }
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in DetectAndHandleType2Failure: {ex.Message}");
                return false;
            }
        }

        public override async Task NavigateAndFillForm(CaseData? data, Dictionary<string, object?>? jsonData)
        {
            try
            {
                LogSystemResources();
                if (data == null)
                {
                    throw new ArgumentNullException(nameof(data));
                }
                if (string.IsNullOrWhiteSpace(data.RecordId))
                {
                    throw new ArgumentNullException(nameof(data.RecordId), "RecordId is required");
                }
                if (string.IsNullOrWhiteSpace(data.FormType))
                {
                    throw new ArgumentNullException(nameof(data.FormType), "FormType is required");
                }
                _logger.LogInformation($"Navigating to IRS EIN form for record_id: {data.RecordId}");
                if (Driver == null)
                {
                    _logger.LogWarning("Driver was null; calling InitializeDriver()");
                    InitializeDriver();

                    if (Driver == null)
                    {
                        _logger.LogCritical("Driver still null after InitializeDriver()");
                        throw new InvalidOperationException("WebDriver is not initialized after InitializeDriver().");
                    }
                }
                Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                Driver.Navigate().GoToUrl("https://sa.www4.irs.gov/modiein/individual/index.jsp");
                _logger.LogInformation("Navigated to IRS EIN form");

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    var alert = new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(d =>
                    {
                        try { return d.SwitchTo().Alert(); } catch (NoAlertPresentException) { return null; }
                    });
                    if (alert != null)
                    {
                        var alertText = alert.Text;
                        alert.Accept();
                        _logger.LogInformation($"Handled alert popup: {alertText}");
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogDebug("No alert popup appeared");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), 10);
                    _logger.LogInformation("Page loaded successfully");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                    _logger.LogError($"Page load timeout. Current URL: {Driver?.Url ?? "N/A"}, Page source: {pageSource}");
                    throw new AutomationError("Page load timeout", "Failed to locate Begin Application button");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), "Begin Application"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to click Begin Application", "Button click unsuccessful after retries");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.Id("individual-leftcontent"), 10);
                    _logger.LogInformation("Main form content loaded");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to load main form content", "Element 'individual-leftcontent' not found");
                }

                var entityType = data.EntityType?.Trim() ?? string.Empty;
                var mappedType = EntityTypeMapping.GetValueOrDefault(entityType, string.Empty);
                var radioId = RadioButtonMapping.GetValueOrDefault(mappedType, string.Empty);
                if (string.IsNullOrEmpty(radioId) || !SelectRadio(radioId, $"Entity type: {mappedType}"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError($"Failed to select entity type: {mappedType}", $"Radio ID: {radioId}");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after entity type", "Continue button click unsuccessful");
                }

                if (!new[] { "Limited Liability Company (LLC)", "Estate" }.Contains(mappedType))
                {
                    var subType = SubTypeMapping.GetValueOrDefault(entityType, "Other");
                    if (entityType == "Non-Profit Corporation")
                    {
                        var businessDesc = data.BusinessDescription?.ToLower() ?? string.Empty;
                        var nonprofitKeywords = new[] { "non-profit", "nonprofit", "charity", "charitable", "501(c)", "tax-exempt" };
                        subType = nonprofitKeywords.Any(keyword => businessDesc.Contains(keyword)) 
                            ? "Non-Profit/Tax-Exempt Organization" 
                            : "Other";
                    }
                    var subTypeRadioId = SubTypeButtonMapping.GetValueOrDefault(subType, "other_option");
                    if (!SelectRadio(subTypeRadioId, $"Sub-type: {subType}"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to select sub-type: {subType}", $"Radio ID: {subTypeRadioId}");
                    }
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue sub-type (first click)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after sub-type selection (first click)");
                    }
                    await Task.Delay(500);
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue sub-type (second click)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after sub-type selection (second click)");
                    }
                }
                else
                {
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after entity type"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after entity type");
                    }
                }

                if (mappedType == "Limited Liability Company (LLC)")
                {
                    // --- numberOfMembers robust handling ---
                    int llcMembers = 1;
                    var llcMembersRaw = data.LlcDetails?.NumberOfMembers;
                    llcMembers = ParseFlexibleInt(llcMembersRaw, 1);
                    if (llcMembers < 1) llcMembers = 1;
                    try
                    {
                        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                        var field = WaitHelper.WaitUntilExists(Driver, By.XPath("//input[@id='numbermem' or @name='numbermem']"), 10);
                        ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", field);
                        field.Clear();
                        await Task.Delay(200);
                        field.SendKeys(llcMembers.ToString());
                        _logger.LogInformation($"Filled LLC members: {llcMembers}");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill LLC members", "Timeout waiting for element");
                    }
                    catch (NoSuchElementException ex)
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill LLC members", ex.Message);
                    }
                    var stateValue = NormalizeState(data.EntityState ?? data.EntityStateRecordState ?? string.Empty);
                    if (!SelectDropdown(By.Id("state"), stateValue, "State"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to select state: {stateValue}");
                    }
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after LLC members and state");
                    }
                }

                var specificStates = new HashSet<string> { "AZ", "CA", "ID", "LA", "NV", "NM", "TX", "WA", "WI" };

                if (mappedType == "Limited Liability Company (LLC)" &&
                    specificStates.Contains(NormalizeState(data.EntityState ?? string.Empty)))
                {
                    try
                    {
                        var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(5));
                        var radioElement = wait.Until(driver =>
                        {
                            var elements = driver.FindElements(By.Id("radio_n"));
                            return elements.Count > 0 ? elements[0] : null;
                        });

                        if (radioElement != null)
                        {
                            if (!SelectRadio("radio_n", "Non-partnership LLC option"))
                            {
                                CaptureBrowserLogs();
                                throw new AutomationError("Failed to select non-partnership LLC option");
                            }

                            if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after radio_n"))
                            {
                                CaptureBrowserLogs();
                                throw new AutomationError("Failed to continue after non-partnership LLC option");
                            }
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("'radio_n' not found within 5 seconds. Skipping selection and continuing.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Unexpected error while handling non-partnership LLC option: {ex.Message}");
                    }

                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after confirmation"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after confirmation");
                    }
                }
                else
                {
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after LLC"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after LLC");
                    }
                }

                if (!SelectRadio("newbiz", "New Business"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select new business");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after business purpose");
                }


                var defaults = GetDefaults(data);
                var firstName = data.EntityMembers?.GetValueOrDefault<string, string>("first_name_1", (string?)defaults["first_name"]!) ?? (string?)defaults["first_name"] ?? string.Empty;
                var lastName = data.EntityMembers?.GetValueOrDefault<string, string>("last_name_1", (string?)defaults["last_name"]!) ?? (string?)defaults["last_name"] ?? string.Empty;
                var middleName = data.EntityMembers?.GetValueOrDefault<string, string>("middle_name_1", (string?)defaults["middle_name"]!) ?? (string?)defaults["middle_name"] ?? string.Empty;

                if (new[] { "Sole Proprietorship", "Individual" }.Contains(data.EntityType ?? string.Empty))
                {
                    if (!FillField(By.Id("applicantFirstName"), firstName, "First Name (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill First Name: {firstName}");
                    }
                    if (!string.IsNullOrEmpty(middleName) && !FillField(By.Id("applicantMiddleName"), middleName, "Middle Name (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Middle Name: {middleName}");
                    }
                    if (!FillField(By.Id("applicantLastName"), lastName, "Last Name (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Last Name: {lastName}");
                    }
                }
                else
                {
                    if (!FillField(By.Id("responsiblePartyFirstName"), firstName, "First Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill First Name: {firstName}");
                    }
                    if (!string.IsNullOrEmpty(middleName) && !FillField(By.Id("responsiblePartyMiddleName"), middleName, "Middle Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Middle Name: {middleName}");
                    }
                    if (!FillField(By.Id("responsiblePartyLastName"), lastName, "Last Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to fill Last Name: {lastName}");
                    }
                }

                var ssn = (string?)defaults["ssn_decrypted"] ?? string.Empty;
                ssn = ssn.Replace("-", "");
                if (new[] { "Sole Proprietorship", "Individual" }.Contains(data.EntityType ?? string.Empty))
                {
                    if (!FillField(By.Id("applicantSSN3"), ssn.Substring(0, 3), "SSN First 3 (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN First 3");
                    }
                    if (!FillField(By.Id("applicantSSN2"), ssn.Substring(3, 2), "SSN Middle 2 (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Middle 2");
                    }
                    if (!FillField(By.Id("applicantSSN4"), ssn.Substring(5), "SSN Last 4 (Applicant)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Last 4");
                    }
                }
                else
                {
                    if (!FillField(By.Id("responsiblePartySSN3"), ssn.Substring(0, 3), "SSN First 3"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN First 3");
                    }
                    if (!FillField(By.Id("responsiblePartySSN2"), ssn.Substring(3, 2), "SSN Middle 2"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Middle 2");
                    }
                    if (!FillField(By.Id("responsiblePartySSN4"), ssn.Substring(5), "SSN Last 4"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill SSN Last 4");
                    }
                }

                if (!SelectRadio("iamsole", "I Am Sole"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select I Am Sole");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after responsible party");
                }

                var address1 = defaults["business_address_1"]?.ToString()?.Trim() ?? string.Empty;
                var address2 = defaults["business_address_2"]?.ToString()?.Trim() ?? string.Empty;
                var fullAddress = string.Join(" ", new[] { address1, address2 }.Where(s => !string.IsNullOrEmpty(s)));

                if (!FillField(By.Id("physicalAddressStreet"), fullAddress, "Street"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical Street");
                }


                if (!FillField(By.Id("physicalAddressCity"), defaults["city"]?.ToString(), "Physical City"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical City");
                }

                if (!SelectDropdown(By.Id("physicalAddressState"), NormalizeState(data.EntityState ?? string.Empty), "Physical State"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Physical State");
                }

                if (!FillField(By.Id("physicalAddressZipCode"), defaults["zip_code"]?.ToString(), "Physical Zip"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical Zip");
                }

                var phone = defaults["phone"]?.ToString() ?? "2812173123";
                var phoneClean = Regex.Replace(phone, @"\D", "");
                if (phoneClean.Length == 10)
                {
                    if (!FillField(By.Id("phoneFirst3"), phoneClean.Substring(0, 3), "Phone First 3"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Phone First 3");
                    }
                    if (!FillField(By.Id("phoneMiddle3"), phoneClean.Substring(3, 3), "Phone Middle 3"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Phone Middle 3");
                    }
                    if (!FillField(By.Id("phoneLast4"), phoneClean.Substring(6), "Phone Last 4"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Phone Last 4");
                    }
                }

                var allowedEntityTypes = new[] { "C-Corporation", "S-Corporation", "Professional Corporation", "Corporation" };
                if (!string.IsNullOrEmpty(data.CareOfName) && allowedEntityTypes.Contains(data.EntityType ?? string.Empty))
                {
                    try
                    {
                        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                        WaitHelper.WaitUntilExists(Driver, By.Id("physicalAddressCareofName"), 10);
                        if (!FillField(By.Id("physicalAddressCareofName"), data.CareOfName, "Physical Care of Name"))
                        {
                            _logger.LogWarning("Failed to fill Physical Care of Name, proceeding");
                        }
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("Physical Care of Name field not found");
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogInformation($"Physical Care of Name field not found: {ex.Message}");
                    }
                }

                // Support MailingAddress as array
                var mailingAddressDict = (data.MailingAddress != null && data.MailingAddress.Count > 0)
                    ? data.MailingAddress[0]
                    : new Dictionary<string, string>();

                var mailingStreet = mailingAddressDict.GetValueOrDefault("mailingStreet", "").Trim();
                var physicalStreet1 = defaults["business_address_1"]?.ToString()?.Trim() ?? string.Empty;
                var physicalStreet2 = defaults["business_address_2"]?.ToString()?.Trim() ?? string.Empty;
                var physicalFullAddress = string.Join(" ", new[] { physicalStreet1, physicalStreet2 }.Where(s => !string.IsNullOrEmpty(s))).Trim();

                var shouldFillMailing = !string.IsNullOrEmpty(mailingStreet) &&
                                        !string.Equals(mailingStreet, physicalFullAddress, StringComparison.OrdinalIgnoreCase);

                if (shouldFillMailing)
                {
                    if (!SelectRadio("radioAnotherAddress_y", "Address option (Yes)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Address option (Yes)");
                    }
                }
                else
                {
                    if (!SelectRadio("radioAnotherAddress_n", "Address option (No)"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Address option (No)");
                    }
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after address option"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after address option");
                }

                try
                {
                    var shortWait = new WebDriverWait(Driver, TimeSpan.FromSeconds(20));
                    var element = WaitHelper.WaitUntilClickable(Driver, By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']"), 20);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    element.Click();
                    _logger.LogInformation("Clicked Accept As Entered");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Accept As Entered button not found within 20 seconds, proceeding.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Unexpected error while clicking Accept As Entered: {ex.Message}");
                }

                if (shouldFillMailing)
                {
                    if (!FillField(By.Id("mailingAddressStreet"), mailingStreet, "Mailing Street"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing Street");
                    }
                    if (!FillField(By.Id("mailingAddressCity"), mailingAddressDict.GetValueOrDefault("mailingCity", ""), "Mailing City"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing City");
                    }
                    if (!FillField(By.Id("mailingAddressState"), mailingAddressDict.GetValueOrDefault("mailingState", ""), "Mailing State"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Mailing State");
                    }
                    if (!FillField(By.Id("mailingAddressPostalCode"), mailingAddressDict.GetValueOrDefault("mailingZip", ""), "Zip"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing Zip");
                    }
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after mailing address"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to continue after mailing address");
                    }
                    try
                    {
                        var shortWait = new WebDriverWait(Driver, TimeSpan.FromSeconds(20));
                        var element = WaitHelper.WaitUntilClickable(Driver, By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']"), 20);
                        ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                        element.Click();
                        _logger.LogInformation("Clicked Accept As Entered");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("Accept As Entered button not found within 20 seconds, proceeding.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Unexpected error while clicking Accept As Entered: {ex.Message}");
                    }
                }


                var suffixRulesByGroup = new Dictionary<string, string[]>
                {
                    {"sole", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"partnerships", new[] {"Corp", "LLC", "PLLC", "LC", "Inc", "PA"}},
                    {"corporations", new[] {"LLC", "PLLC", "LC"}},
                    {"limited", new[] {"Corp", "Inc", "PA"}},
                    {"trusts", new[] {"Corp", "LLC", "PLLC", "LC", "Inc", "PA"}},
                    {"estate", new[] {"Corp", "LLC", "PLLC", "LC", "Inc", "PA"}},
                    {"viewadditional", new string[] {}}
                };

                string? businessName;
                try
                {
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                    var originalName = businessName;

                    var entityTypeLabel = data.EntityType?.Trim() ?? string.Empty;
                    mappedType = EntityTypeMapping.GetValueOrDefault(entityTypeLabel, string.Empty).Trim();
                    var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType);

                    if (!string.IsNullOrEmpty(entityGroup))
                    {
                        var suffixes = suffixRulesByGroup.GetValueOrDefault(entityGroup, new string[] {});
                        foreach (var suffix in suffixes)
                        {
                            if (Regex.IsMatch(businessName, $@"\b{suffix}\s*$", RegexOptions.IgnoreCase))
                            {
                                businessName = Regex.Replace(businessName, $@"\b{suffix}\s*$", "", RegexOptions.IgnoreCase).Trim();
                                _logger.LogInformation($"Stripped suffix '{suffix}' from business name: '{originalName}' -> '{businessName}'");
                                break;
                            }
                        }
                    }

                    businessName = Regex.Replace(businessName, @"[^\w\s\-&]", "");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to process business name: {ex.Message}");
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                }

                try
                {
                    var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType);
                    if (entityGroup == "sole")
                    {
                        if (!FillField(By.Id("businessOperationalTradeName"), businessName, "Trade Name (Sole Prop/Individual)"))
                        {
                            _logger.LogInformation("Failed to fill business name in trade name field");
                        }
                    }
                    else
                    {
                        if (!FillField(By.Id("businessOperationalLegalName"), businessName, "Legal Business Name"))
                        {
                            _logger.LogInformation("Failed to fill business name in legal name field");
                        }
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Business name field not found");
                }
                catch (NoSuchElementException ex)
                {
                    _logger.LogInformation($"Business name field not found: {ex.Message}");
                }

                if (!FillField(By.Id("businessOperationalCounty"), NormalizeState(data.EntityState ?? string.Empty), "County"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill County");
                }

                try
                {
                    SelectDropdown(By.Id("businessOperationalState"), NormalizeState(data.County ?? string.Empty), "Business Operational State");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Business Operational State dropdown not found");
                }
                catch (NoSuchElementException ex)
                {
                    _logger.LogInformation($"Business Operational State dropdown not found: {ex.Message}");
                }

                var entityTypesRequiringArticles = new[] { "C-Corporation", "S-Corporation", "Professional Corporation", "Corporation", "Limited Liability Company", "Professional Limited Liability Company", "Limited Liability Company (LLC)", "Professional Limited Liability Company (PLLC)", "LLC" };
                if (entityTypesRequiringArticles.Contains(data.EntityType ?? string.Empty))
                {
                    try
                    {
                        SelectDropdown(By.Id("articalsFiledState"), NormalizeState(data.County ?? string.Empty), "Articles Filed State");
                        _logger.LogInformation("Selected Articles Filed State");
                    }
                    catch (WebDriverTimeoutException)
                    {
                        _logger.LogInformation("Articles Filed State dropdown not found");
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogInformation($"Articles Filed State dropdown not found: {ex.Message}");
                    }
                }

        try
        {
            if (!string.IsNullOrEmpty(data.TradeName))
            {
                var tradeName = data.TradeName?.Trim() ?? string.Empty;
                var entityName = (string?)defaults["entity_name"] ?? string.Empty;

                var entityTypeLabel = data.EntityType?.Trim() ?? string.Empty;
                var localMappedType = EntityTypeMapping.GetValueOrDefault(entityTypeLabel, string.Empty).Trim();
                var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType, "");

                var suffixesByGroup = new Dictionary<string, string[]>
                {
                    {"sole", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"partnerships", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"corporations", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"limited", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"trusts", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"estate", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                    {"viewadditional", new string[] {}}
                };

                string NormalizeName(string name, string group)
                {
                    string result = Regex.Replace(name, @"[^\w\s\-&]", "").Trim();
                    var suffixes = suffixesByGroup.GetValueOrDefault(group, new string[] { });

                    foreach (var suffix in suffixes)
                    {
                        if (Regex.IsMatch(result, $@"\b{suffix}\s*$", RegexOptions.IgnoreCase))
                        {
                            result = Regex.Replace(result, $@"\b{suffix}\s*$", "", RegexOptions.IgnoreCase).Trim();
                            break;
                        }
                    }

                    return result;
                }

                var normalizedTrade = NormalizeName(tradeName, entityGroup);
                var normalizedEntity = NormalizeName(entityName, entityGroup);

                if (!string.Equals(normalizedTrade, normalizedEntity, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Filling Trade Name since it differs from Entity Name: '{normalizedTrade}' != '{normalizedEntity}'");

                    if (!FillField(By.Id("businessOperationalTradeName"), normalizedTrade, "Trade Name"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Trade Name");
                    }
                }
                else
                {
                    _logger.LogInformation("Skipping Trade Name input as it's same as Entity Name after normalization.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not process or fill Trade Name: {ex.Message}");
        }


                // --- startDate robust handling ---
                var (month, year) = ParseFlexibleDate(data?.FormationDate ?? string.Empty);
                if (!SelectDropdown(By.Id("BUSINESS_OPERATIONAL_MONTH_ID"), month?.ToString(), "Formation Month"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Formation Month");
                }
                if (!FillField(By.Id("BUSINESS_OPERATIONAL_YEAR_ID"), year.ToString(), "Formation Year"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Formation Year");
                }

                // --- closingMonth robust handling ---
                string closingMonthRaw = data.ClosingMonth?.ToString() ?? string.Empty;
                if (int.TryParse(closingMonthRaw, out var closingMonthInt))
                    closingMonthRaw = closingMonthInt.ToString();

                if (!string.IsNullOrEmpty(closingMonthRaw))
                {
                    var monthMapping = new Dictionary<string, string>
                    {
                        {"january", "JANUARY"}, {"jan", "JANUARY"}, {"1", "JANUARY"},
                        {"february", "FEBRUARY"}, {"feb", "FEBRUARY"}, {"2", "FEBRUARY"},
                        {"march", "MARCH"}, {"mar", "MARCH"}, {"3", "MARCH"},
                        {"april", "APRIL"}, {"apr", "APRIL"}, {"4", "APRIL"},
                        {"may", "MAY"}, {"5", "MAY"},
                        {"june", "JUNE"}, {"jun", "JUNE"}, {"6", "JUNE"},
                        {"july", "JULY"}, {"jul", "JULY"}, {"7", "JULY"},
                        {"august", "AUGUST"}, {"aug", "AUGUST"}, {"8", "AUGUST"},
                        {"september", "SEPTEMBER"}, {"sep", "SEPTEMBER"}, {"9", "SEPTEMBER"},
                        {"october", "OCTOBER"}, {"oct", "OCTOBER"}, {"10", "OCTOBER"},
                        {"november", "NOVEMBER"}, {"nov", "NOVEMBER"}, {"11", "NOVEMBER"},
                        {"december", "DECEMBER"}, {"dec", "DECEMBER"}, {"12", "DECEMBER"}
                    };

                    var entityTypesRequiringFiscalMonth = new[] { "Partnership", "Joint venture", "Limited Partnership", "General partnership", "C-Corporation", "Limited Liability Partnership", "LLP", "Corporation" };

                    if (entityTypesRequiringFiscalMonth.Contains(data.EntityType ?? string.Empty))
                    {
                        var normalizedMonth = monthMapping.GetValueOrDefault(closingMonthRaw.ToLower().Trim());
                        if (!string.IsNullOrEmpty(normalizedMonth))
                        {
                            const int retries = 2;
                            for (int attempt = 0; attempt < retries; attempt++)
                            {
                                try
                                {
                                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                                    var dropdown = WaitHelper.WaitUntilClickable(Driver, By.Id("fiscalMonth"), 10);
                                    new SelectElement(dropdown).SelectByText(normalizedMonth);
                                    _logger.LogInformation($"Selected Fiscal Month: {normalizedMonth}");
                                    break;
                                }
                                catch (WebDriverTimeoutException)
                                {
                                    if (attempt < retries - 1)
                                    {
                                        _logger.LogWarning($"Attempt {attempt + 1} to select Fiscal Month failed");
                                        await Task.Delay(1000);
                                    }
                                    else
                                    {
                                        CaptureBrowserLogs();
                                        throw new AutomationError($"Failed to select Fiscal Month {normalizedMonth}");
                                    }
                                }
                                catch (NoSuchElementException ex)
                                {
                                    if (attempt < retries - 1)
                                    {
                                        _logger.LogWarning($"Attempt {attempt + 1} to select Fiscal Month failed: {ex.Message}");
                                        await Task.Delay(1000);
                                    }
                                    else
                                    {
                                        CaptureBrowserLogs();
                                        throw new AutomationError($"Failed to select Fiscal Month {normalizedMonth}", ex.Message);
                                    }
                                }
                                catch (StaleElementReferenceException ex)
                                {
                                    if (attempt < retries - 1)
                                    {
                                        _logger.LogWarning($"Attempt {attempt + 1} to select Fiscal Month failed: {ex.Message}");
                                        await Task.Delay(1000);
                                    }
                                    else
                                    {
                                        CaptureBrowserLogs();
                                        throw new AutomationError($"Failed to select Fiscal Month {normalizedMonth}", ex.Message);
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Invalid closing_month: {closingMonthRaw}, skipping");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Skipping fiscal month selection for entity_type: {data.EntityType}");
                    }
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after formation date");
                }

                var activityRadios = new[] { "radioTrucking_n", "radioInvolveGambling_n", "radioExciseTax_n", "radioSellTobacco_n", "radioHasEmployees_n" };
                foreach (var radio in activityRadios)
                {
                    if (!SelectRadio(radio, radio))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError($"Failed to select {radio}");
                    }
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after activity options");
                }

                if (!SelectRadio("other", "Other activity"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Other activity");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after primary activity");
                }

                if (!SelectRadio("other", "Other service"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Other service");
                }
                if (!FillField(By.Id("pleasespecify"), defaults["business_description"]?.ToString(), "Business Description"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Business Description");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after specify service");
                }

                if (!SelectRadio("receiveonline", "Receive Online"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Receive Online");
                }

                if (ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after receive EIN"))
                {
                    CaptureBrowserLogs();
                   var (blobUrl, success) = await CapturePageAsPdf(data, CancellationToken.None);

                    if (success && !string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation($"Confirmation screenshot uploaded to Azure: {blobUrl}");
                    }
                    else
                    {
                        _logger.LogError("Failed to capture and upload EIN confirmation PDF.");
                    }
                }
                else
                {
                    throw new AutomationError("Failed to continue after receive EIN selection");
                }

                _logger.LogInformation("Form filled successfully");
            }
            catch (WebDriverTimeoutException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (NoSuchElementException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (ElementNotInteractableException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (StaleElementReferenceException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (WebDriverException ex)
            {
                CaptureBrowserLogs();
                if (File.Exists(DriverLogPath))
                {
                    try
                    {
                        var driverLogs = await File.ReadAllTextAsync(DriverLogPath);
                        _logger.LogError($"ChromeDriver logs: {driverLogs}");
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogError($"Failed to read ChromeDriver logs: {logEx.Message}");
                    }
                }
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"WebDriver error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("WebDriver error", ex.Message);
            }
            catch (Exception ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Unexpected error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");

                try
                {
                    var handled = await DetectAndHandleType2Failure(data, jsonData);
                    if (handled)
                    {
                        _logger.LogWarning("Handled as Type 2 EIN failure during form fill. Skipping exception raise.");
                        return;
                    }
                }
                catch (Exception err)
                {
                    _logger.LogError($"Type 2 handler failed while processing EIN failure page: {err.Message}");
                }

                throw new AutomationError("Unexpected form filling error", ex.Message);
            }
            
        }

        public override async Task HandleTrusteeshipEntity(CaseData? data)
        {
            _logger.LogInformation("Handling Trusteeship entity type form flow");
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (string.IsNullOrWhiteSpace(data.EntityType))
            {
                throw new ArgumentNullException(nameof(data.EntityType), "EntityType is required");
            }
            var defaults = GetDefaults(data);

            try
            {
                LogSystemResources();
                _logger.LogInformation($"Navigating to IRS EIN form for record_id: {data.RecordId}");
                if (Driver == null)
                {
                    _logger.LogWarning("Driver was null; calling InitializeDriver()");
                    InitializeDriver();

                    if (Driver == null)
                    {
                        _logger.LogCritical("Driver still null after InitializeDriver()");
                        throw new InvalidOperationException("WebDriver is not initialized after InitializeDriver().");
                    }
                }
                Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                Driver.Navigate().GoToUrl("https://sa.www4.irs.gov/modiein/individual/index.jsp");
                _logger.LogInformation("Navigated to IRS EIN form");

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    var alert = new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(d =>
                    {
                        try { return d.SwitchTo().Alert(); } catch (NoAlertPresentException) { return null; }
                    });
                    if (alert != null)
                    {
                        var alertText = alert.Text;
                        alert.Accept();
                        _logger.LogInformation($"Handled alert popup: {alertText}");
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogDebug("No alert popup appeared");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), 10);
                    _logger.LogInformation("Page loaded successfully");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                    _logger.LogError($"Page load timeout. Current URL: {Driver?.Url ?? "N/A"}, Page source: {pageSource}");
                    throw new AutomationError("Page load timeout", "Failed to locate Begin Application button");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']"), "Begin Application"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to click Begin Application", "Button click unsuccessful after retries");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    WaitHelper.WaitUntilExists(Driver, By.Id("individual-leftcontent"), 10);
                    _logger.LogInformation("Main form content loaded");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to load main form content", "Element 'individual-leftcontent' not found");
                }

                var entityType = data.EntityType?.Trim() ?? string.Empty;
                var mappedType = EntityTypeMapping.GetValueOrDefault(entityType, "Trusts");
                _logger.LogInformation($"Mapped entity type: {entityType} -> {mappedType}");
                var radioId = RadioButtonMapping.GetValueOrDefault(mappedType);
                if (string.IsNullOrEmpty(radioId) || !SelectRadio(radioId, $"Entity type: {mappedType}"))
                {
                    throw new AutomationError("Failed to select Trusteeship entity type");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after entity type"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after entity type");
                }

                var subType = SubTypeMapping.GetValueOrDefault(entityType, "Other");
                var subRadioId = SubTypeButtonMapping.GetValueOrDefault(subType, "other_option");
                if (!SelectRadio(subRadioId, $"Sub-type: {subType}"))
                {
                    throw new AutomationError("Failed to select Trusteeship sub-type");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after sub-type"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after sub-type");
                }
                CaptureBrowserLogs();
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after sub-type"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after sub-type");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='responsiblePartyFirstName']"), (string?)defaults["first_name"] ?? string.Empty, "Responsible First Name"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Responsible First Name");
                }
                CaptureBrowserLogs();

                if (!string.IsNullOrEmpty((string?)defaults["middle_name"]) && !FillField(By.XPath("//input[@id='responsiblePartyMiddleName']"), (string?)defaults["middle_name"] ?? string.Empty, "Responsible Middle Name"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Responsible Middle Name");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='responsiblePartyLastName']"), (string?)defaults["last_name"] ?? string.Empty, "Responsible Last Name"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Responsible Last Name");
                }
                CaptureBrowserLogs();

                var ssn = (string?)defaults["ssn_decrypted"] ?? string.Empty;
                ssn = ssn.Replace("-", "");
                if (!FillField(By.XPath("//input[@id='responsiblePartySSN3']"), ssn.Substring(0, 3), "SSN First 3"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill SSN First 3");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='responsiblePartySSN2']"), ssn.Substring(3, 2), "SSN Middle 2"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill SSN Middle 2");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='responsiblePartySSN4']"), ssn.Substring(5), "SSN Last 4"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill SSN Last 4");
                }
                CaptureBrowserLogs();

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit2' and contains(@value, 'Continue >>')]"), "Continue after SSN"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after SSN");
                }
                CaptureBrowserLogs();

                FillField(By.XPath("//input[@id='responsiblePartyFirstName']"), (string?)defaults["first_name"] ?? string.Empty, "Clear & Fill First Name");
                CaptureBrowserLogs();
                if (!string.IsNullOrEmpty((string?)defaults["middle_name"]))
                {
                    FillField(By.XPath("//input[@id='responsiblePartyMiddleName']"), (string?)defaults["middle_name"] ?? string.Empty, "Responsible Middle Name");
                }
                CaptureBrowserLogs();
                FillField(By.XPath("//input[@id='responsiblePartyLastName']"), (string?)defaults["last_name"] ?? string.Empty, "Clear & Fill Last Name");
                CaptureBrowserLogs();

                if (!SelectRadio("iamsole", "I Am Sole"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select I Am Sole");
                }
                CaptureBrowserLogs();
                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit' and contains(@value, 'Continue >>')]"), "Continue after I Am Sole"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after I Am Sole");
                }
                CaptureBrowserLogs();

                // Trusteeship entity: update mailing address usage
                var mailingAddressDict = (data.MailingAddress != null && data.MailingAddress.Count > 0) ? data.MailingAddress[0] : new Dictionary<string, string>();
                if (!FillField(By.XPath("//input[@id='mailingAddressStreet']"), mailingAddressDict.GetValueOrDefault("mailingStreet", ""), "Mailing Street"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing Street");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='mailingAddressCity']"), mailingAddressDict.GetValueOrDefault("mailingCity", ""), "Mailing City"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing City");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='mailingAddressState']"), mailingAddressDict.GetValueOrDefault("mailingState", ""), "Mailing State"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing State");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='mailingAddressPostalCode']"), mailingAddressDict.GetValueOrDefault("mailingZip", ""), "Zip"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Zip");
                }
                CaptureBrowserLogs();

                if (!FillField(By.XPath("//input[@id='internationalPhoneNumber']"), (string?)defaults["phone"] ?? string.Empty, "Phone Number"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Phone Number");
                }
                CaptureBrowserLogs();

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit' and contains(@value, 'Continue >>')]"), "Continue after Mailing"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after Mailing");
                }
                CaptureBrowserLogs();

                try
                {
                    var shortWait = new WebDriverWait(Driver, TimeSpan.FromSeconds(20));
                    var element = WaitHelper.WaitUntilClickable(Driver, By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']"), 20);
                    ((IJavaScriptExecutor)Driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);
                    element.Click();
                    _logger.LogInformation("Clicked Accept As Entered");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Accept As Entered button not found within 20 seconds, proceeding.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Unexpected error while clicking Accept As Entered: {ex.Message}");
                }

                string? businessName;
                try
                {
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                    businessName = Regex.Replace(businessName, @"[^\w\s\-&]", "");
                    var suffixes = new[] { "Corp", "Inc", "LLC", "LC", "PLLC", "PA", "L.L.C.", "INC.", "CORPORATION", "LIMITED" };
                    var pattern = $@"\b(?:{string.Join("|", suffixes.Select(Regex.Escape))})\b\.?$";
                    businessName = Regex.Replace(businessName, pattern, "", RegexOptions.IgnoreCase).Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to process business name: {ex.Message}");
                    businessName = ((string?)defaults["entity_name"] ?? string.Empty).Trim();
                }

                try
                {
                    if (!FillField(By.Id("businessOperationalLegalName"), businessName, "Legal Business Name"))
                    {
                        _logger.LogInformation("Failed to fill business name in appropriate field based on entity type");
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogInformation("Business name field not found");
                }
                catch (NoSuchElementException ex)
                {
                    _logger.LogInformation($"Business name field not found: {ex.Message}");
                }

                if (!FillField(By.XPath("//input[@id='businessOperationalCounty']"), data.EntityState ?? string.Empty, "County"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill County");
                }
                CaptureBrowserLogs();

                try
            {
                var normalizedState = NormalizeState(data.County ?? string.Empty);
                var stateSelect = WaitHelper.WaitUntilClickable(Driver, By.XPath("//select[@id='businessOperationalState' and @name='businessOperationalState']"), 10);
                new SelectElement(stateSelect).SelectByValue(normalizedState);
                _logger.LogInformation($"Selected state: {normalizedState}");
            }
            catch (WebDriverTimeoutException)
            {
                CaptureBrowserLogs();
                throw new AutomationError("Failed to select state");
            }
            catch (NoSuchElementException ex)
            {
                CaptureBrowserLogs();
                throw new AutomationError($"Failed to select state: {ex.Message}");
            }
            CaptureBrowserLogs();


                var (month, year) = ParseFlexibleDate(data.FormationDate ?? string.Empty);
                if (!SelectDropdown(By.Id("BUSINESS_OPERATIONAL_MONTH_ID"), month.ToString(), "Formation Month"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Formation Month");
                }
                if (!FillField(By.Id("BUSINESS_OPERATIONAL_YEAR_ID"), year.ToString(), "Formation Year"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Formation Year");
                }

                if (!ClickButton(By.XPath("//input[@type='submit' and @name='Submit' and contains(@value, 'Continue >>')]"), "Continue after Business Info"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after Business Info");
                }
                CaptureBrowserLogs();

                if (!SelectRadio("radioHasEmployees_n", "radioHasEmployees_n"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select radioHasEmployees_n");
                }
                if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to continue after activity options");
                }
                CaptureBrowserLogs();

                if (!SelectRadio("receiveonline", "Receive Online"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to select Receive Online");
                }

                if (ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), "Continue after receive EIN"))
                {
                    CaptureBrowserLogs();
                    var (blobUrl, success) = await CapturePageAsPdf(data, CancellationToken.None);

                    if (success && !string.IsNullOrEmpty(blobUrl))
                    {
                        _logger.LogInformation($"Confirmation screenshot uploaded to Azure: {blobUrl}");
                    }
                    else
                    {
                        _logger.LogError("Failed to capture and upload EIN confirmation PDF.");
                    }
                }
                else
                {
                    throw new AutomationError("Failed to continue after receive EIN selection");
                }

                _logger.LogInformation("Form filled successfully");
                _logger.LogInformation("Completed Trusteeship entity form successfully");
            }
            catch (WebDriverTimeoutException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (NoSuchElementException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (ElementNotInteractableException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (StaleElementReferenceException ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Form filling error at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("Form filling error", ex.Message);
            }
            catch (WebDriverException ex)
            {
                CaptureBrowserLogs();
                if (File.Exists(DriverLogPath))
                {
                    try
                    {
                        var driverLogs = await File.ReadAllTextAsync(DriverLogPath);
                        _logger.LogError($"ChromeDriver logs: {driverLogs}");
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogError($"Failed to read ChromeDriver logs: {logEx.Message}");
                    }
                }
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"WebDriver error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");
                throw new AutomationError("WebDriver error", ex.Message);
            }
            catch (Exception ex)
            {
                CaptureBrowserLogs();
                var pageSource = Driver?.PageSource?.Substring(0, Math.Min(1000, Driver.PageSource.Length)) ?? "N/A";
                _logger.LogError($"Unexpected error during form filling at URL: {Driver?.Url ?? "N/A"}, Error: {ex.Message}, Page source: {pageSource}");

                try
                {
                    var handled = await DetectAndHandleType2Failure(data, new Dictionary<string, object?>());
                    if (handled)
                    {
                        _logger.LogWarning("Handled as Type 2 EIN failure during form fill. Skipping exception raise.");
                        return;
                    }
                }
                catch (Exception err)
                {
                    _logger.LogError($"Type 2 handler failed while processing EIN failure page: {err.Message}");
                }

                throw new AutomationError("Unexpected form filling error", ex.Message);
            }
        }

        public override async Task<(bool Success, string? Message, string? AzureBlobUrl)> RunAutomation(CaseData? data, Dictionary<string, object> jsonData)
        {
            string? einNumber = string.Empty;
            string? pdfAzureUrl = null;
            bool success = false;

            try
            {
                await _salesforceClient.InitializeSalesforceAuthAsync();

                var missingFields = data.GetType()
                    .GetProperties()
                    .Where(p => p.GetValue(data) == null && p.Name != "RecordId")
                    .Select(p => p.Name)
                    .ToList();
                if (missingFields.Any())
                {
                    _logger.LogInformation($"Missing fields detected (using defaults): {string.Join(", ", missingFields)}");
                    jsonData["missing_fields"] = missingFields;
                }

                InitializeDriver();

                if (string.IsNullOrWhiteSpace(data.EntityType))
                {
                    throw new ArgumentNullException(nameof(data.EntityType), "EntityType is required");
                }
                if (data.EntityType == "Trusteeship")
                {
                    await HandleTrusteeshipEntity(data);
                }
                else
                {
                    await NavigateAndFillForm(data, jsonData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                }

                // _______________________________________________final submit deployment ________________________________


                 // // 5. Continue to EIN Letter
                // if (!ClickButton(By.XPath("//input[@type='submit' and @value='Continue >>']"), 
                //         "Final Continue before EIN download"))
                //     {
                //         throw new Exception("Failed to click final Continue button before EIN");
                //     }


                // (einNumber, pdfAzureUrl, success) = await FinalSubmit(data, jsonData, CancellationToken.None);
                
                

                 // Type "yes" to click final submit button____________________________local testing_____________________


                Console.WriteLine("Type 'yes' to continue to the EIN letter step:");
                string? input = Console.ReadLine();


                if (input?.Trim().ToLower() == "yes")
                {
                    if (!ClickButton(By.XPath("//input[@type='submit' and @value='Submit']"),
                        "Final Continue before EIN download"))
                    {
                        throw new Exception("Failed to click final Continue button before EIN");
                    }
                }
                else
                {
                    throw new Exception("User did not confirm with 'yes'. Aborting EIN letter step.");
                }
                
                // Type "yes" to download the EIN Letter

                Console.WriteLine("Type 'yes' to proceed with final EIN submission:");
                input = Console.ReadLine(); 

                if (input?.Trim().ToLower() == "yes")
                {
                    (einNumber, pdfAzureUrl, success) = await FinalSubmit(data, jsonData, CancellationToken.None);
                }
                else
                {
                    throw new Exception("User did not confirm with 'yes'. Aborting FinalSubmit.");
                }
                

                if (success && !string.IsNullOrEmpty(data.RecordId) && !string.IsNullOrEmpty(einNumber))
                {
                    await _salesforceClient.NotifySalesforceSuccessAsync(data.RecordId, einNumber);
                }

                return (success, einNumber, pdfAzureUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation failed");

                string errorMsg = string.Empty;
                if (Driver != null)
                    errorMsg = _errorMessageExtractionService.ExtractErrorMessage(Driver);
                if (string.IsNullOrWhiteSpace(errorMsg))
                    errorMsg = ex.Message ?? "Unknown failure";
                if (jsonData != null)
                    jsonData["error_message"] = errorMsg;

                try
                {
                    var (failureBlobUrl, failureSuccess) = await CaptureFailurePageAsPdf(data, CancellationToken.None);
                    if (failureSuccess)
                    {
                        if (!string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId, "500", "fail", Driver);
                        }
                    }
                    // Always save JSON with error message
                    if (jsonData != null)
                        await _blobStorageService.SaveJsonDataSync(jsonData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? new object()));
                }
                catch (Exception pdfError)
                {
                    _logger.LogWarning($"Failed to capture or upload failure PDF/JSON: {pdfError.Message}");
                }

                return (false, null, null);
            }
            finally
            {
                try
                {
                    var recordId = data.RecordId ?? "unknown";
                    var logPath = DriverLogPath ?? string.Empty;
                    var logUrl = await _blobStorageService.UploadLogToBlob(recordId, logPath);
                    if (!string.IsNullOrEmpty(logUrl))
                    {
                        _logger.LogInformation($"Uploaded Chrome log to: {logUrl}");
                    }
                }
                catch (Exception logError)
                {
                    _logger.LogWarning($"Failed to upload Chrome logs: {logError.Message}");
                }

                Cleanup();
            }
        }

        private async Task<(string? EinNumber, string? PdfAzureUrl, bool Success)> FinalSubmit(CaseData? data, Dictionary<string, object>? jsonData, CancellationToken cancellationToken)
        {
            string? einNumber = null;
            string? pdfAzureUrl = null;

            try
            {
                if (Driver == null)
                {
                    _logger.LogError("Driver is null");
                    return (null, null, false);
                }

                // Try to extract EIN
                try
                {
                    var einElement = WaitHelper.WaitUntilExists(Driver, By.CssSelector("td[align='left'] > b"), 10);
                    var einText = einElement?.Text?.Trim();

                    if (!string.IsNullOrEmpty(einText) && Regex.IsMatch(einText, @"^\d{2}-\d{7}$"))
                    {
                        einNumber = einText;
                    }
                }
                catch (Exception)
                {
                    _logger.LogWarning("Primary EIN extraction failed. Attempting fallback with HtmlAgilityPack.");
                    var doc = new HtmlDocument();
                    doc.LoadHtml(Driver.PageSource);

                    var einNode = doc.DocumentNode.SelectSingleNode("//td/b[contains(text(), '-')]");
                    if (einNode != null)
                    {
                        var einText = einNode.InnerText.Trim();
                        if (Regex.IsMatch(einText, @"^\d{2}-\d{7}$"))
                        {
                            einNumber = einText;
                            _logger.LogInformation("Extracted EIN via fallback: {EinNumber}", einNumber);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(einNumber) && jsonData != null)
                {
                    jsonData["einNumber"] = einNumber;
                }

                // Handle failure page
                if (string.IsNullOrEmpty(einNumber))
                {
                    string pageText = Driver?.PageSource?.ToLower() ?? string.Empty;

                    if (pageText.Contains("we are unable to provide you with an ein"))
                    {
                        string referenceNumber = null;

                        // Primary attempt: regex on raw page text
                        var refMatch = Regex.Match(pageText, @"reference number\s+(\d+)");
                        if (refMatch.Success)
                        {
                            referenceNumber = refMatch.Groups[1].Value;
                            _logger.LogInformation("Extracted IRS Reference Number: {ReferenceNumber}", referenceNumber);
                        }
                        else
                        {
                            _logger.LogWarning("Primary reference number extraction failed. Attempting fallback with HtmlAgilityPack.");

                            try
                            {
                                var doc = new HtmlDocument();
                                doc.LoadHtml(Driver.PageSource);

                                var textNodes = doc.DocumentNode.SelectNodes("//text()[contains(., 'reference number')]");
                                if (textNodes != null)
                                {
                                    foreach (var node in textNodes)
                                    {
                                        var match = Regex.Match(node.InnerText, @"reference number\s+(\d+)");
                                        if (match.Success)
                                        {
                                            referenceNumber = match.Groups[1].Value;
                                            _logger.LogInformation("Extracted IRS Reference Number via fallback: {ReferenceNumber}", referenceNumber);
                                            break;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("Fallback parsing of reference number failed: {Message}", ex.Message);
                            }
                        }

                        if (!string.IsNullOrEmpty(referenceNumber) && jsonData != null)
                        {
                            jsonData["irs_reference_number"] = referenceNumber;
                        }

                        await CaptureFailurePageAsPdf(data, cancellationToken);
                        await _salesforceClient.NotifySalesforceErrorCodeAsync(data?.RecordId, referenceNumber ?? "fail", "fail", Driver);
                        return (null, null, false);
                    }
                    else
                    {
                        await CaptureFailurePageAsPdf(data, cancellationToken);
                        await _salesforceClient.NotifySalesforceErrorCodeAsync(data?.RecordId, "500", "fail", Driver);
                        return (null, null, false);
                    }
                }

                // Try downloading EIN Letter PDF
                try
                {
                    string? pdfUrl = null;

                    try
                    {
                        var downloadLink = WaitHelper.WaitUntilClickable(Driver, By.XPath("//a[contains(text(), 'EIN Confirmation Letter') and contains(@href, '.pdf')]"), 10);

                        pdfUrl = downloadLink.GetAttribute("href");
                    }
                    catch (Exception)
                    {
                        _logger.LogWarning("Primary EIN PDF link extraction failed. Trying fallback.");
                        var doc = new HtmlDocument();
                        doc.LoadHtml(Driver.PageSource);

                        var linkNode = doc.DocumentNode
                            .SelectSingleNode("//a[contains(text(), 'EIN Confirmation Letter') and contains(@href, '.pdf')]");

                        pdfUrl = linkNode?.GetAttributeValue("href", null);
                    }

                    if (string.IsNullOrEmpty(pdfUrl))
                    {
                        throw new Exception("PDF URL is null or empty after all attempts");
                    }

                    if (pdfUrl.StartsWith("/"))
                        pdfUrl = "https://sa.www4.irs.gov" + pdfUrl;

                    _logger.LogInformation("Attempting direct download from URL: {PdfUrl}", pdfUrl);
                    var response = await _httpClient.GetAsync(pdfUrl);

                    if (response.IsSuccessStatusCode &&
                        response.Content.Headers.ContentType?.MediaType?.StartsWith("application/pdf") == true)
                    {
                        var pdfBytes = await response.Content.ReadAsByteArrayAsync();

                        var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                        var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINLetter.pdf";
                        pdfAzureUrl = await _blobStorageService.UploadFinalBytesToBlob(pdfBytes, blobName, "application/pdf", cancellationToken);

                        if (jsonData != null)
                            await _blobStorageService.SaveJsonDataSync(jsonData);

                        await _salesforceClient.NotifyEinLetterToSalesforceAsync(data?.RecordId, pdfAzureUrl, data?.EntityName);
                        await _salesforceClient.NotifySalesforceSuccessAsync(data?.RecordId, einNumber);

                        return (einNumber, pdfAzureUrl, true);
                    }

                    throw new Exception("PDF download failed or response is not a PDF");
                }
                catch (Exception ex)
                {
                    _logger.LogError("PDF download failed: {Message}", ex.Message);

                    if (jsonData != null)
                        await _blobStorageService.SaveJsonDataSync(jsonData);

                    await CaptureFailurePageAsPdf(data, cancellationToken);
                    await _salesforceClient.NotifySalesforceErrorCodeAsync(data?.RecordId, einNumber, "fail", Driver);
                    return (einNumber, null, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in FinalSubmit");
                return (null, null, false);
            }
        }

        // Helper to robustly parse date from string or DateTime
        private static (int? month, int? year) ParseFlexibleDate(object? dateObj)
        {
            if (dateObj == null) return (null, null);
            DateTime dt;
            if (dateObj is DateTime d)
                dt = d;
            else if (dateObj is string s && DateTime.TryParse(s, out var parsed))
                dt = parsed;
            else
                return (null, null);
            return (dt.Month, dt.Year);
        }

        // Helper to robustly parse int from string or number
        private static int ParseFlexibleInt(object? value, int defaultValue = 0)
        {
            if (value == null) return defaultValue;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (value is string s && int.TryParse(s, out var result)) return result;
            return defaultValue;
        }

        // Helper to robustly parse double from string or number
        private static double? ParseFlexibleDouble(object? value)
        {
            if (value == null) return null;
            if (value is double d) return d;
            if (value is float f) return (double)f;
            if (value is int i) return (double)i;
            if (value is long l) return (double)l;
            if (value is string s && double.TryParse(s, out var result)) return result;
            return null;
        }

        // Helper to robustly parse bool from string or bool
        private static bool ParseFlexibleBool(object? value, bool defaultValue = false)
        {
            if (value == null) return defaultValue;
            if (value is bool b) return b;
            if (value is string s)
            {
                if (bool.TryParse(s, out var result)) return result;
                if (s == "1" || s.ToLower() == "yes" || s.ToLower() == "y" || s.ToLower() == "true") return true;
                if (s == "0" || s.ToLower() == "no" || s.ToLower() == "n" || s.ToLower() == "false") return false;
            }
            if (value is int i) return i != 0;
            return defaultValue;
        }
    }
}

