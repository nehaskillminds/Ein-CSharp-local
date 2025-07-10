// LlcDetails.cs
namespace EinAutomation.Api.Models;

public class LlcDetails
{
    public string? NumberOfMembers { get; set; }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "NumberOfMembers", NumberOfMembers ?? string.Empty }
        };
    }
}