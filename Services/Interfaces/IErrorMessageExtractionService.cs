using OpenQA.Selenium;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IErrorMessageExtractionService
    {
        string ExtractErrorMessage(IWebDriver driver);
        string ExtractErrorMessage(string htmlContent);
    }
}