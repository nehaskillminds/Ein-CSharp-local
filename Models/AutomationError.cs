// AutomationError.cs
namespace EinAutomation.Api.Models;

public class AutomationError : Exception
{
    public string? Details { get; }

    public AutomationError(string message, string? details = null) 
        : base(message)
    {
        Details = details;
    }

    public AutomationError(string message, string? details, Exception innerException) 
        : base(message, innerException)
    {
        Details = details;
    }
}