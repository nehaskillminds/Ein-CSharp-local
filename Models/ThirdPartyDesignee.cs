// ThirdPartyDesignee.cs
namespace EinAutomation.Api.Models;

public class ThirdPartyDesignee
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? Authorized { get; set; }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "Name", Name ?? string.Empty },
            { "Phone", Phone ?? string.Empty },
            { "Fax", Fax ?? string.Empty },
            { "Authorized", Authorized ?? string.Empty }
        };
    }
}