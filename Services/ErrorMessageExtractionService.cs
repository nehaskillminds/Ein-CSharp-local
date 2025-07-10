using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using System.Text.RegularExpressions;

namespace EinAutomation.Api.Services
{
    public class ErrorMessageExtractionService : IErrorMessageExtractionService
    {
        private readonly ILogger<ErrorMessageExtractionService> _logger;

        public ErrorMessageExtractionService(ILogger<ErrorMessageExtractionService> logger)
        {
            _logger = logger;
        }

        public string ExtractErrorMessage(IWebDriver driver)
        {
            try
            {
                _logger.LogInformation("Attempting to extract error message from page");

                // First, try to find the error container by ID
                var errorContainer = driver.FindElements(By.Id("errorListId")).FirstOrDefault();
                if (errorContainer != null)
                {
                    return ExtractErrorFromContainer(errorContainer);
                }

                // If not found by ID, try to find by class name
                var errorElements = driver.FindElements(By.ClassName("validation_error_text"));
                if (errorElements.Any())
                {
                    return ExtractErrorFromElements(errorElements);
                }

                // Try to find by partial text match
                var errorByText = driver.FindElements(By.XPath("//*[contains(text(), 'Error(s) has occurred')]"));
                if (errorByText.Any())
                {
                    return ExtractErrorFromErrorSection(errorByText.First());
                }

                _logger.LogWarning("No error messages found on the page");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while extracting error message from page");
                return string.Empty;
            }
        }

        public string ExtractErrorMessage(string htmlContent)
        {
            try
            {
                _logger.LogInformation("Attempting to extract error message from HTML content");

                if (string.IsNullOrEmpty(htmlContent))
                {
                    _logger.LogWarning("HTML content is null or empty");
                    return string.Empty;
                }

                // Pattern to match error messages within validation_error_text elements
                var patterns = new[]
                {
                    // Pattern for anchor tags with error messages
                    @"<a[^>]*href=""[^""]*""[^>]*style=""[^""]*color:\s*[`'""]?#990000[`'""]?[^""]*""[^>]*>([^<]+)</a>",
                    
                    // Pattern for li elements with validation_error_text class
                    @"<li[^>]*class=""[^""]*validation_error_text[^""]*""[^>]*>(?:<a[^>]*>)?([^<]+)(?:</a>)?</li>",
                    
                    // Pattern for any element with validation_error_text class (excluding the header)
                    @"<[^>]*class=""[^""]*validation_error_text[^""]*""[^>]*>(?!Error\(s\) has occurred:)([^<]+)</[^>]*>",
                    
                    // Fallback pattern to capture text after "Error(s) has occurred:"
                    @"Error\(s\) has occurred:[^<]*<[^>]*>([^<]+)"
                };

                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(htmlContent, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (matches.Count > 0)
                    {
                        var errorMessages = matches
                            .Cast<Match>()
                            .Select(m => m.Groups[1].Value.Trim())
                            .Where(msg => !string.IsNullOrEmpty(msg) && 
                                         !msg.Equals("Error(s) has occurred:", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (errorMessages.Any())
                        {
                            var combinedMessage = string.Join("; ", errorMessages);
                            _logger.LogInformation("Extracted error message: {ErrorMessage}", combinedMessage);
                            return combinedMessage;
                        }
                    }
                }

                _logger.LogWarning("No error messages found in HTML content");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while extracting error message from HTML content");
                return string.Empty;
            }
        }

        private string ExtractErrorFromContainer(IWebElement errorContainer)
        {
            try
            {
                // Get the parent container that contains all error elements
                var parentElement = errorContainer.FindElement(By.XPath(".."));
                
                // Find all anchor elements with error styling
                var errorLinks = parentElement.FindElements(By.XPath(".//a[contains(@style, '#990000')]"));
                
                if (errorLinks.Any())
                {
                    var errorMessages = errorLinks
                        .Select(link => link.Text.Trim())
                        .Where(text => !string.IsNullOrEmpty(text))
                        .ToList();

                    if (errorMessages.Any())
                    {
                        var combinedMessage = string.Join("; ", errorMessages);
                        _logger.LogInformation("Extracted error message from container: {ErrorMessage}", combinedMessage);
                        return combinedMessage;
                    }
                }

                // Fallback: get all li elements with validation_error_text class
                var errorItems = parentElement.FindElements(By.XPath(".//li[@class='validation_error_text']"));
                if (errorItems.Any())
                {
                    var errorMessages = errorItems
                        .Select(item => item.Text.Trim())
                        .Where(text => !string.IsNullOrEmpty(text))
                        .ToList();

                    if (errorMessages.Any())
                    {
                        var combinedMessage = string.Join("; ", errorMessages);
                        _logger.LogInformation("Extracted error message from list items: {ErrorMessage}", combinedMessage);
                        return combinedMessage;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from error container");
                return string.Empty;
            }
        }

        private string ExtractErrorFromElements(IList<IWebElement> errorElements)
        {
            try
            {
                var errorMessages = new List<string>();

                foreach (var element in errorElements)
                {
                    var text = element.Text.Trim();
                    
                    // Skip the header text
                    if (text.Contains("Error(s) has occurred:"))
                        continue;

                    // Check if this element contains anchor links with error styling
                    var errorLinks = element.FindElements(By.XPath(".//a[contains(@style, '#990000')]"));
                    if (errorLinks.Any())
                    {
                        foreach (var link in errorLinks)
                        {
                            var linkText = link.Text.Trim();
                            if (!string.IsNullOrEmpty(linkText))
                            {
                                errorMessages.Add(linkText);
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(text))
                    {
                        errorMessages.Add(text);
                    }
                }

                if (errorMessages.Any())
                {
                    var combinedMessage = string.Join("; ", errorMessages);
                    _logger.LogInformation("Extracted error message from elements: {ErrorMessage}", combinedMessage);
                    return combinedMessage;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from error elements");
                return string.Empty;
            }
        }

        private string ExtractErrorFromErrorSection(IWebElement errorSection)
        {
            try
            {
                // Get the parent container
                var parentElement = errorSection.FindElement(By.XPath(".."));
                
                // Look for anchor elements with error styling
                var errorLinks = parentElement.FindElements(By.XPath(".//a[contains(@style, '#990000')]"));
                
                if (errorLinks.Any())
                {
                    var errorMessages = errorLinks
                        .Select(link => link.Text.Trim())
                        .Where(text => !string.IsNullOrEmpty(text))
                        .ToList();

                    if (errorMessages.Any())
                    {
                        var combinedMessage = string.Join("; ", errorMessages);
                        _logger.LogInformation("Extracted error message from error section: {ErrorMessage}", combinedMessage);
                        return combinedMessage;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting message from error section");
                return string.Empty;
            }
        }
    }
}