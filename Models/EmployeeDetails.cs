// EmployeeDetails.cs
namespace EinAutomation.Api.Models;

public class EmployeeDetails
{
    public string? Other { get; set; }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "Other", Other ?? string.Empty }
        };
    }
}