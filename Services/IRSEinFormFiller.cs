using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.Chrome;
using SeleniumExtras.WaitHelpers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

namespace EinAutomation.Api.Services
{
    public class IRSEinFormFiller : EinFormFiller
    {
        private readonly ISalesforceClient _salesforceClient;
        private readonly HttpClient _httpClient;

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
            HttpClient? httpClient)
            : base(logger ?? throw new ArgumentNullException(nameof(logger)), 
                   blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService)))
        {
            _salesforceClient = salesforceClient ?? throw new ArgumentNullException(nameof(salesforceClient));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
                    var refMatch = Regex.Match(pageText, @"reference number\s+(\d+)");
                    if (refMatch.Success)
                    {
                        var referenceNumber = refMatch.Groups[1].Value;
                        if (jsonData != null)
                        {
                            var nonNullableJsonData = jsonData?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? new object()) 
                                ?? new Dictionary<string, object>();
                            nonNullableJsonData["irs_reference_number"] = referenceNumber;
                            await _blobStorageService.SaveJsonDataSync(nonNullableJsonData);
                        }
                        if (data != null && !string.IsNullOrEmpty(data.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId, referenceNumber, "fail");
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
                if (data.EntityType == null)
                {
                    throw new ArgumentNullException(nameof(data.EntityType));
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
                    var alert = new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(ExpectedConditions.AlertIsPresent());
                    var alertText = alert.Text;
                    alert.Accept();
                    _logger.LogInformation($"Handled alert popup: {alertText}");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogDebug("No alert popup appeared");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                        ExpectedConditions.ElementExists(
                            By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']")));
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
                    new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                        ExpectedConditions.ElementExists(By.Id("individual-leftcontent")));
                    _logger.LogInformation("Main form content loaded");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to load main form content", "Element 'individual-leftcontent' not found");
                }

                var entityType = data.EntityType.Trim();
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
                    int llcMembers = 1;
                    if (data.LlcDetails?.NumberOfMembers != null)
                    {
                        try
                        {
                            llcMembers = Convert.ToInt32(data.LlcDetails.NumberOfMembers);
                            if (llcMembers < 1) llcMembers = 1;
                        }
                        catch (Exception)
                        {
                            _logger.LogWarning($"Invalid LLC members value: {data.LlcDetails.NumberOfMembers}, using default: 1");
                        }
                    }
                    try
                    {
                        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                        var field = new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                            ExpectedConditions.ElementToBeClickable(
                                By.XPath("//input[@id='numbermem' or @name='numbermem']")));
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
                    var stateValue = NormalizeState(data.EntityState ?? data.EntityStateRecordState);
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
                if (mappedType == "Limited Liability Company (LLC)" && specificStates.Contains(NormalizeState(data.EntityState)))
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

                if (new[] { "Sole Proprietorship", "Individual" }.Contains(data.EntityType))
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
                if (new[] { "Sole Proprietorship", "Individual" }.Contains(data.EntityType))
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

                if (!FillField(By.Id("physicalAddressStreet"), defaults["business_address_1"]?.ToString(), "Street"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical Street");
                }

                if (!FillField(By.Id("physicalAddressCity"), defaults["city"]?.ToString(), "Physical City"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Physical City");
                }

                if (!SelectDropdown(By.Id("physicalAddressState"), NormalizeState(data.EntityState), "Physical State"))
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
                if (!string.IsNullOrEmpty(data.CareOfName) && allowedEntityTypes.Contains(data.EntityType))
                {
                    try
                    {
                        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                        new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                            ExpectedConditions.ElementExists(By.Id("physicalAddressCareofName")));
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

                var mailingAddress = data.MailingAddress ?? new Dictionary<string, string>();
                var hasMailingAddress = !string.IsNullOrEmpty(mailingAddress.GetValueOrDefault("mailingStreet", "").Trim());
                if (hasMailingAddress)
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
                    var element = shortWait.Until(ExpectedConditions.ElementToBeClickable(
                        By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']")));
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

                if (hasMailingAddress)
                {
                    if (!FillField(By.Id("mailingAddressStreet"), mailingAddress.GetValueOrDefault("mailingStreet", ""), "Mailing Street"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing Street");
                    }
                    if (!FillField(By.Id("mailingAddressCity"), mailingAddress.GetValueOrDefault("mailingCity", ""), "Mailing City"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to fill Mailing City");
                    }
                    if (!FillField(By.Id("mailingAddressState"), mailingAddress.GetValueOrDefault("mailingState", ""), "Mailing State"))
                    {
                        CaptureBrowserLogs();
                        throw new AutomationError("Failed to select Mailing State");
                    }
                    if (!FillField(By.Id("mailingAddressPostalCode"), mailingAddress.GetValueOrDefault("mailingZip", ""), "Zip"))
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
                        var element = shortWait.Until(ExpectedConditions.ElementToBeClickable(
                            By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']")));
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

                    var entityTypeLabel = data.EntityType.Trim();
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

                if (!FillField(By.Id("businessOperationalCounty"), NormalizeState(data.EntityState), "County"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill County");
                }

                try
                {
                    SelectDropdown(By.Id("businessOperationalState"), NormalizeState(data.County), "Business Operational State");
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
                if (entityTypesRequiringArticles.Contains(data.EntityType))
                {
                    try
                    {
                        SelectDropdown(By.Id("articalsFiledState"), NormalizeState(data.County), "Articles Filed State");
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
                        var tradeName = data.TradeName.Trim();
                        var originalTrade = tradeName;

                        var entityTypeLabel = data.EntityType.Trim();
                        mappedType = EntityTypeMapping.GetValueOrDefault(entityTypeLabel, string.Empty).Trim();
                        var entityGroup = RadioButtonMapping.GetValueOrDefault(mappedType);

                        var tradeSuffixRulesByGroup = new Dictionary<string, string[]>
                        {
                            {"sole", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                            {"partnerships", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                            {"corporations", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                            {"limited", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                            {"trusts", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                            {"estate", new[] {"LLC", "LC", "PLLC", "PA", "Corp", "Inc"}},
                            {"viewadditional", new string[] {}}
                        };

                        if (!string.IsNullOrEmpty(entityGroup))
                        {
                            foreach (var suffix in tradeSuffixRulesByGroup.GetValueOrDefault(entityGroup, new string[] {}))
                            {
                                if (Regex.IsMatch(tradeName, $@"\b{suffix}\s*$", RegexOptions.IgnoreCase))
                                {
                                    tradeName = Regex.Replace(tradeName, $@"\b{suffix}\s*$", "", RegexOptions.IgnoreCase).Trim();
                                    _logger.LogInformation($"Stripped suffix '{suffix}' from trade name: {originalTrade}' -> '{tradeName}'");
                                    break;
                                }
                            }
                        }

                        tradeName = Regex.Replace(tradeName, @"[^\w\s\-&]", "");
                        if (!FillField(By.Id("businessOperationalTradeName"), tradeName, "Trade Name"))
                        {
                            CaptureBrowserLogs();
                            throw new AutomationError("Failed to fill Trade Name");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not process or fill Trade Name: {ex.Message}");
                }

                var (month, year) = ParseFormationDate(data?.FormationDate ?? string.Empty);
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

                if (!string.IsNullOrEmpty(data.ClosingMonth))
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

                    if (entityTypesRequiringFiscalMonth.Contains(data.EntityType))
                    {
                        var normalizedMonth = monthMapping.GetValueOrDefault(data.ClosingMonth.ToLower().Trim());
                        if (!string.IsNullOrEmpty(normalizedMonth))
                        {
                            const int retries = 2;
                            for (int attempt = 0; attempt < retries; attempt++)
                            {
                                try
                                {
                                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                                    var dropdown = new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                                        ExpectedConditions.ElementToBeClickable(By.Id("fiscalMonth")));
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
                            _logger.LogWarning($"Invalid closing_month: {data.ClosingMonth}, skipping");
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
                    _logger.LogInformation($"Confirmation screenshot uploaded to Azure: {blobUrl}");
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
            if (data.EntityType == null)
            {
                throw new ArgumentNullException(nameof(data.EntityType));
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
                    var alert = new WebDriverWait(Driver, TimeSpan.FromSeconds(5)).Until(ExpectedConditions.AlertIsPresent());
                    var alertText = alert.Text;
                    alert.Accept();
                    _logger.LogInformation($"Handled alert popup: {alertText}");
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogDebug("No alert popup appeared");
                }

                try
                {
                    Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(Timeout);
                    new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                        ExpectedConditions.ElementExists(
                            By.XPath("//input[@type='submit' and @name='submit' and @value='Begin Application >>']")));
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
                    new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                        ExpectedConditions.ElementExists(By.Id("individual-leftcontent")));
                    _logger.LogInformation("Main form content loaded");
                }
                catch (WebDriverTimeoutException)
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to load main form content", "Element 'individual-leftcontent' not found");
                }

                var entityType = data.EntityType.Trim();
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

                var mailingAddress = data.MailingAddress ?? new Dictionary<string, string>();
                if (!FillField(By.XPath("//input[@id='mailingAddressStreet']"), mailingAddress.GetValueOrDefault("mailingStreet", ""), "Mailing Street"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing Street");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='mailingAddressCity']"), mailingAddress.GetValueOrDefault("mailingCity", ""), "Mailing City"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing City");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='mailingAddressState']"), mailingAddress.GetValueOrDefault("mailingState", ""), "Mailing State"))
                {
                    CaptureBrowserLogs();
                    throw new AutomationError("Failed to fill Mailing State");
                }
                CaptureBrowserLogs();
                if (!FillField(By.XPath("//input[@id='mailingAddressPostalCode']"), mailingAddress.GetValueOrDefault("mailingZip", ""), "Zip"))
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
                    var element = shortWait.Until(ExpectedConditions.ElementToBeClickable(
                        By.XPath("//input[@type='submit' and @name='Submit' and @value='Accept As Entered']")));
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
                    var stateSelect = new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                        ExpectedConditions.ElementToBeClickable(By.XPath("//select[@id='businessOperationalState' and @name='businessOperationalState']")));
                    new SelectElement(stateSelect).SelectByValue(data.County?.Substring(0, 2).ToUpper() ?? string.Empty);
                    _logger.LogInformation($"Selected state: {data.County?.Substring(0, 2).ToUpper() ?? string.Empty}");
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

                var (month, year) = ParseFormationDate(data.FormationDate ?? string.Empty);
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
                    _logger.LogInformation($"Confirmation screenshot uploaded to Azure: {blobUrl}");
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

        public override async Task<(bool Success, string? Message, string? AzureBlobUrl)> RunAutomation(CaseData? data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var jsonData = new Dictionary<string, object>
            {
                {"record_id", data.RecordId ?? string.Empty},
                {"form_type", data.FormType ?? string.Empty},
                {"entity_name", data.EntityName ?? string.Empty},
                {"entity_type", data.EntityType ?? string.Empty},
                {"formation_date", data.FormationDate ?? string.Empty},
                {"business_category", data.BusinessCategory ?? string.Empty},
                {"business_description", data.BusinessDescription ?? string.Empty},
                {"business_address_1", data.BusinessAddress1 ?? string.Empty},
                {"entity_state", data.EntityState ?? string.Empty},
                {"business_address_2", data.BusinessAddress2 ?? string.Empty},
                {"city", data.City ?? string.Empty},
                {"zip_code", data.ZipCode ?? string.Empty},
                {"quarter_of_first_payroll", data.QuarterOfFirstPayroll ?? string.Empty},
                {"entity_state_record_state", data.EntityStateRecordState ?? string.Empty},
                {"case_contact_name", data.CaseContactName ?? string.Empty},
                {"ssn_decrypted", data.SsnDecrypted ?? string.Empty},
                {"proceed_flag", data.ProceedFlag ?? string.Empty},
                {"entity_members", data.EntityMembers ?? new Dictionary<string, string>()},
                {"locations", data.Locations ?? new List<Dictionary<string, object>>()},
                {"mailing_address", data.MailingAddress ?? new Dictionary<string, string>()},
                {"county", data.County ?? string.Empty},
                {"trade_name", data.TradeName ?? string.Empty},
                {"care_of_name", data.CareOfName ?? string.Empty},
                {"closing_month", data.ClosingMonth ?? string.Empty},
                {"filing_requirement", data.FilingRequirement ?? string.Empty},
                {"employee_details", data.EmployeeDetails?.ToDictionary() ?? new Dictionary<string, object>()},
                {"third_party_designee", data.ThirdPartyDesignee?.ToDictionary() ?? new Dictionary<string, object>()},
                {"llc_details", data.LlcDetails?.ToDictionary() ?? new Dictionary<string, object>()},
                {"missing_fields", new List<string>()},
                {"response_status", string.Empty}
            };

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

                if (data.EntityType == "Trusteeship")
                {
                    await HandleTrusteeshipEntity(data);
                }
                else
                {
                    await NavigateAndFillForm(data, jsonData);
                }

                (einNumber, pdfAzureUrl, success) = await FinalSubmit(data, jsonData);

                if (success && !string.IsNullOrEmpty(data.RecordId) && !string.IsNullOrEmpty(einNumber))
                {
                    await _salesforceClient.NotifySalesforceSuccessAsync(data.RecordId, einNumber);
                }

                return (success, einNumber, pdfAzureUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automation failed");

                try
                {
                    var cleanName = Regex.Replace(data.EntityName ?? "UnknownEntity", @"[^\w]", "");
                    var blobName = $"EntityProcess/{data.RecordId ?? "unknown"}/{cleanName}-ID-EINSubmissionFailure.pdf";

                    if (Driver == null)
                    {
                        _logger.LogError("Driver is null");
                        return (false, null, null);
                    }

                    var pdfData = ((IJavaScriptExecutor)Driver).ExecuteScript("return window.printToPDF({printBackground: true, preferCSSPageSize: true});");
                    if (pdfData == null)
                    {
                        _logger.LogError("PDF data is null");
                        return (false, null, null);
                    }
                    var pdfBytes = Convert.FromBase64String((string)pdfData);
                    var blobUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                    _logger.LogInformation($"Uploaded automation failure PDF to: {blobUrl}");

                    if (!ConfirmationUploaded && !string.IsNullOrEmpty(data.RecordId))
                    {
                        await _salesforceClient.NotifyScreenshotUploadToSalesforceAsync(data.RecordId, blobUrl, data.EntityName ?? "UnknownEntity", "EIN_FAILURE");
                        ConfirmationUploaded = true;
                    }
                }
                catch (Exception pdfError)
                {
                    _logger.LogWarning($"Failed to capture or upload failure PDF: {pdfError.Message}");
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

        private async Task<(string? EinNumber, string? PdfAzureUrl, bool Success)> FinalSubmit(CaseData? data, Dictionary<string, object>? jsonData)
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

                try
                {
                    var einElement = new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                        ExpectedConditions.ElementExists(
                            By.CssSelector("td[align='left'] > b")));
                    var einText = einElement?.Text?.Trim();

                    if (!string.IsNullOrEmpty(einText) && Regex.IsMatch(einText, @"^\d{2}-\d{7}$"))
                    {
                        einNumber = einText;
                        if (jsonData != null && einNumber != null)
                        {
                            jsonData["einNumber"] = einNumber;
                            await _blobStorageService.SaveJsonDataSync(jsonData);
                        }
                        _logger.LogInformation($"Extracted EIN: {einNumber}");
                    }
                    else
                    {
                        _logger.LogWarning($"Extracted EIN '{einText}' does not match expected format XX-XXXXXXX");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to extract EIN: {ex.Message}");
                }

                try
                {
                    var pageText = Driver?.PageSource?.ToLower() ?? string.Empty;
                    if (pageText.Contains("we are unable to provide you with an ein"))
                    {
                        _logger.LogWarning("EIN assignment failed. Capturing failure PDF and extracting reference number...");

                        var refMatch = Regex.Match(pageText, @"reference number\s+(\d+)");
                        var referenceNumber = refMatch.Success ? refMatch.Groups[1].Value : null;
                        if (refMatch.Success)
                        {
                            if (jsonData != null)
                            {
                                jsonData["irs_reference_number"] = referenceNumber;
                                _logger.LogInformation($"Extracted IRS Reference Number: {referenceNumber}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Reference number not found on failure page.");
                        }

                        try
                        {
                            var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w]", "");
                            var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINSubmissionFailure.pdf";
                            
                            var pdfData = ((IJavaScriptExecutor)Driver).ExecuteScript("return window.printToPDF({printBackground: true, preferCSSPageSize: true});");
                            if (pdfData == null)
                            {
                                _logger.LogError("PDF data is null");
                                return (null, null, false);
                            }
                            var pdfBytes = Convert.FromBase64String((string)pdfData);
                            pdfAzureUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                            _logger.LogInformation($"Failure PDF uploaded: {pdfAzureUrl}");

                            if (!ConfirmationUploaded && !string.IsNullOrEmpty(data?.RecordId) && !string.IsNullOrEmpty(referenceNumber))
                            {
                                await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId, referenceNumber, "fail");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to capture/upload failure PDF: {ex.Message}");
                        }

                        try
                        {
                            if (jsonData != null)
                            {
                                await _blobStorageService.SaveJsonDataSync(jsonData);
                                _logger.LogInformation("Updated JSON with reference number saved.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to save JSON with reference number: {ex.Message}");
                        }

                        return (null, pdfAzureUrl, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failure page detection or handling failed: {ex.Message}");
                }

                if (!string.IsNullOrEmpty(einNumber))
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceSuccessAsync(data.RecordId, einNumber);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to notify Salesforce: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(einNumber))
                {
                    try
                    {
                        var downloadLink = new WebDriverWait(Driver, TimeSpan.FromSeconds(10)).Until(
                            ExpectedConditions.ElementToBeClickable(
                                By.XPath("//a[contains(text(), 'EIN Confirmation Letter') and contains(@href, '.pdf')]")));
                        var pdfUrl = downloadLink.GetAttribute("href");

                        if (string.IsNullOrEmpty(pdfUrl))
                        {
                            throw new Exception("PDF URL is null or empty");
                        }

                        if (pdfUrl.StartsWith("/"))
                        {
                            pdfUrl = "https://sa.www4.irs.gov" + pdfUrl;
                        }

                        var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w\-]", "").Replace(" ", "");
                        var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINLetter.pdf";

                        _logger.LogInformation($"Attempting direct download from URL: {pdfUrl}");
                        var response = await _httpClient.GetAsync(pdfUrl);
                        if (response?.Content?.Headers?.ContentType?.MediaType == null)
                        {
                            throw new Exception("Invalid response content");
                        }
                        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType.MediaType.StartsWith("application/pdf"))
                        {
                            var pdfBytes = await response.Content.ReadAsByteArrayAsync();
                            pdfAzureUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");
                            _logger.LogInformation($"PDF uploaded to Azure Blob Storage: {pdfAzureUrl}");
                            if (!string.IsNullOrEmpty(data?.RecordId) && !string.IsNullOrEmpty(pdfAzureUrl))
                            {
                                await _salesforceClient.NotifyEinLetterToSalesforceAsync(data.RecordId, pdfAzureUrl, data.EntityName ?? "UnknownEntity");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Unexpected PDF response: {response.StatusCode}, {response.Content.Headers.ContentType}");
                            throw new Exception("Failed to download EIN confirmation letter.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to download or upload PDF: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(einNumber))
                {
                    try
                    {
                        var cleanName = Regex.Replace(data?.EntityName ?? "UnknownEntity", @"[^\w]", "");
                        var blobName = $"EntityProcess/{data?.RecordId ?? "unknown"}/{cleanName}-ID-EINSubmissionFailure.pdf";
                        
                        var pdfData = ((IJavaScriptExecutor)Driver).ExecuteScript("return window.printToPDF({printBackground: true, preferCSSPageSize: true});");
                        if (pdfData == null)
                        {
                            _logger.LogError("PDF data is null");
                            return (null, null, false);
                        }
                        var pdfBytes = Convert.FromBase64String((string)pdfData);
                        pdfAzureUrl = await _blobStorageService.UploadBytesToBlob(pdfBytes, blobName, "application/pdf");

                        if (!ConfirmationUploaded && !string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifyScreenshotUploadToSalesforceAsync(data.RecordId, pdfAzureUrl, data?.EntityName ?? "UnknownEntity", "EIN_FAILURE");
                        }

                        if (!string.IsNullOrEmpty(data?.RecordId))
                        {
                            await _salesforceClient.NotifySalesforceErrorCodeAsync(data.RecordId, "500");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to handle generic failure notification: {ex.Message}");
                    }
                }

                return (einNumber, pdfAzureUrl, einNumber != null);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error in FinalSubmit: {ex.Message}");
                return (null, null, false);
            
            }
        }
    }
}