using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;

namespace EinAutomation.Api.Infrastructure
{
    public static class WaitHelper
    {
        public static IWebElement WaitUntilVisible(IWebDriver driver, By locator, int timeoutSeconds = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return wait.Until(d =>
            {
                var element = d.FindElement(locator);
                return element.Displayed ? element : null;
            });
        }

        public static IWebElement WaitUntilClickable(IWebDriver driver, By locator, int timeoutSeconds = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return wait.Until(d =>
            {
                var element = d.FindElement(locator);
                return (element != null && element.Displayed && element.Enabled) ? element : null;
            });
        }

        public static bool WaitUntilTitleContains(IWebDriver driver, string titleFragment, int timeoutSeconds = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return wait.Until(d => d.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase));
        }

        public static bool WaitUntilUrlChanges(IWebDriver driver, string currentUrl, int timeoutSeconds = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return wait.Until(d => d.Url != currentUrl);
        }

        public static bool WaitUntilElementGone(IWebDriver driver, By locator, int timeoutSeconds = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return wait.Until(d =>
            {
                try
                {
                    return !d.FindElement(locator).Displayed;
                }
                catch (NoSuchElementException)
                {
                    return true;
                }
            });
        }

        public static IWebElement WaitUntilExists(IWebDriver driver, By locator, int timeoutSeconds = 10)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return wait.Until(d =>
            {
                try
                {
                    return d.FindElement(locator);
                }
                catch (NoSuchElementException)
                {
                    return null;
                }
            });
        }
    }
} 