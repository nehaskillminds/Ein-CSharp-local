// CaseData.cs
using System.ComponentModel.DataAnnotations;

namespace EinAutomation.Api.Models;

public class CaseData
{
    [Required(ErrorMessage = "RecordId is required")]
    public string RecordId { get; set; } = null!;

    public string? FormType { get; set; }
    public string? EntityName { get; set; }
    public string? EntityType { get; set; }
    public string? FormationDate { get; set; }
    public string? BusinessCategory { get; set; }
    public string? BusinessDescription { get; set; }
    public string? BusinessAddress1 { get; set; }
    public string? EntityState { get; set; }
    public string? BusinessAddress2 { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
    public string? QuarterOfFirstPayroll { get; set; }
    public string? EntityStateRecordState { get; set; }
    public string? CaseContactName { get; set; }
    public string? SsnDecrypted { get; set; }
    public string? ProceedFlag { get; set; } = "true";
    public Dictionary<string, string>? EntityMembers { get; set; }
    public List<Dictionary<string, object>>? Locations { get; set; }
    // Change MailingAddress to support array of addresses
    public List<Dictionary<string, string>>? MailingAddress { get; set; }
    // Add similar property for PhysicalAddress if needed
    public List<Dictionary<string, string>>? PhysicalAddress { get; set; }
    public string? County { get; set; }
    public string? TradeName { get; set; }
    public string? CareOfName { get; set; }
    public string? ClosingMonth { get; set; }
    public string? FilingRequirement { get; set; }
    public EmployeeDetails? EmployeeDetails { get; set; }
    public ThirdPartyDesignee? ThirdPartyDesignee { get; set; }
    public LlcDetails? LlcDetails { get; set; }
}