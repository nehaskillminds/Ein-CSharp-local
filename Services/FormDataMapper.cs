using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Linq;

#nullable enable

namespace EinAutomation.Api.Services;

public sealed class FormDataMapper : IFormDataMapper
{
    private readonly ILogger<FormDataMapper> _logger;
    private static readonly Dictionary<string, string> MonthMapping = new Dictionary<string, string>
    {
        { "1", "JANUARY" }, { "2", "FEBRUARY" }, { "3", "MARCH" }, { "4", "APRIL" },
        { "5", "MAY" }, { "6", "JUNE" }, { "7", "JULY" }, { "8", "AUGUST" },
        { "9", "SEPTEMBER" }, { "10", "OCTOBER" }, { "11", "NOVEMBER" }, { "12", "DECEMBER" }
    };

    public FormDataMapper(ILogger<FormDataMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CaseData MapFormAutomationData(IDictionary<string, object> formData)
    {
        if (formData == null)
        {
            _logger.LogWarning("Form data is null");
            throw new ArgumentNullException(nameof(formData));
        }

        try
        {
            // Extract nested objects, handling nulls
            var responsibleParty = GetDictionaryValue(formData, "responsibleParty") ?? new Dictionary<string, object>();
            var ownershipDetails = GetListValue(formData, "ownershipDetails") ?? new List<Dictionary<string, object>>();
            var physicalAddressList = GetListValue(formData, "physicalAddress") ?? new List<Dictionary<string, object>>();
            var mailingAddressList = GetListValue(formData, "mailingAddress") ?? new List<Dictionary<string, object>>();
            var employeeDetails = GetDictionaryValue(formData, "employeeDetails") ?? new Dictionary<string, object>();
            var thirdParty = GetDictionaryValue(formData, "thirdPartyDesignee") ?? new Dictionary<string, object>();
            var llcDetails = GetDictionaryValue(formData, "llcDetails") ?? new Dictionary<string, object>();
            var entityType = GetStringValue(formData, "entityType")?.Trim() ?? "Limited Liability Company (LLC)";

            // Extract physical address (prefer "Business" locationType)
            var physicalAddress = physicalAddressList.FirstOrDefault(addr => GetStringValue(addr, "locationType") == "Business") 
                ?? physicalAddressList.FirstOrDefault() 
                ?? new Dictionary<string, object>();

            // Extract mailing address (prefer "Mailing" locationType)
            var mailingAddress = mailingAddressList.FirstOrDefault(addr => GetStringValue(addr, "locationType") == "Mailing") 
                ?? mailingAddressList.FirstOrDefault() 
                ?? new Dictionary<string, object>();

            if (!mailingAddress.Any(kvp => !string.IsNullOrWhiteSpace(kvp.Value?.ToString())))
            {
                _logger.LogWarning("Mailing address is empty or contains only empty fields");
            }
            else if (!mailingAddress.Any())
            {
                _logger.LogWarning("No mailing address provided, defaulting to empty");
            }

            // Map closing month
            var closingMonthValue = GetStringValue(formData, "closingMonth");
            var closingMonth = MonthMapping.TryGetValue(closingMonthValue ?? string.Empty, out var month) ? month : null;

            // Map ownership details to entity_members
            var entityMembersDict = new Dictionary<string, string>();
            var responsibleFirstName = GetStringValue(responsibleParty, "firstName")?.Trim() ?? string.Empty;
            var responsibleMiddleName = GetStringValue(responsibleParty, "middleName")?.Trim() ?? string.Empty;
            var responsibleLastName = GetStringValue(responsibleParty, "lastName")?.Trim() ?? string.Empty;

            foreach (var (member, index) in ownershipDetails.Select((m, i) => (m, i + 1)))
            {
                var memberFirstName = GetStringValue(member, "firstName")?.Trim() ?? string.Empty;
                var memberMiddleName = GetStringValue(member, "middleName")?.Trim() ?? string.Empty;
                var memberLastName = GetStringValue(member, "lastName")?.Trim() ?? string.Empty;

                if (string.Equals(memberFirstName, responsibleFirstName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(memberMiddleName, responsibleMiddleName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(memberLastName, responsibleLastName, StringComparison.OrdinalIgnoreCase))
                {
                    entityMembersDict["first_name_1"] = memberFirstName;
                    entityMembersDict["middle_name_1"] = memberMiddleName;
                    entityMembersDict["last_name_1"] = memberLastName;
                    entityMembersDict["phone_1"] = GetStringValue(responsibleParty, "phone")?.Trim() ?? string.Empty;
                    entityMembersDict["name_1"] = $"{memberFirstName} {memberMiddleName} {memberLastName}".Trim();
                    entityMembersDict["percent_ownership_1"] = GetStringValue(member, "ownershipPercentage") ?? string.Empty;
                    break;
                }
            }

            if (!entityMembersDict.Any() && !string.IsNullOrEmpty(responsibleFirstName) && !string.IsNullOrEmpty(responsibleLastName))
            {
                entityMembersDict["first_name_1"] = responsibleFirstName;
                entityMembersDict["middle_name_1"] = responsibleMiddleName;
                entityMembersDict["last_name_1"] = responsibleLastName;
                entityMembersDict["phone_1"] = GetStringValue(responsibleParty, "phone")?.Trim() ?? string.Empty;
                entityMembersDict["name_1"] = $"{responsibleFirstName} {responsibleMiddleName} {responsibleLastName}".Trim();
                entityMembersDict["percent_ownership_1"] = string.Empty;
            }

            // Map locations
            var locations = new List<Dictionary<string, object>> {
                new Dictionary<string, object> {
                    { "physicalStreet", GetStringValue(physicalAddress, "street") ?? string.Empty },
                    { "physicalCity", GetStringValue(physicalAddress, "city") ?? string.Empty },
                    { "physicalState", GetStringValue(physicalAddress, "state") ?? string.Empty },
                    { "physicalZip", GetStringValue(physicalAddress, "zipCode") ?? string.Empty }
                }
            };

            return new CaseData
            {
                RecordId = GetStringValue(formData, "entityProcessId") ?? "temp_record_id",
                FormType = GetStringValue(formData, "formType"),
                EntityName = GetStringValue(formData, "legalName"),
                EntityType = entityType,
                FormationDate = GetStringValue(formData, "startDate"),
                BusinessCategory = GetStringValue(formData, "principalActivity"),
                BusinessDescription = GetStringValue(formData, "principalLineOfBusiness"),
                BusinessAddress1 = GetStringValue(physicalAddress, "street"),
                BusinessAddress2 = GetStringValue(physicalAddress, "street2"),
                EntityState = GetStringValue(physicalAddress, "state"),
                City = GetStringValue(physicalAddress, "city"),
                ZipCode = GetStringValue(physicalAddress, "zipCode"),
                QuarterOfFirstPayroll = GetStringValue(formData, "firstWagesDate"),
                CaseContactName = null,
                SsnDecrypted = GetStringValue(responsibleParty, "ssnOrItinOrEin"),
                ProceedFlag = "true",
                EntityMembers = entityMembersDict,
                Locations = locations,
                MailingAddress = new Dictionary<string, string>
                {
                    { "mailingStreet", GetStringValue(mailingAddress, "street") ?? string.Empty },
                    { "mailingCity", GetStringValue(mailingAddress, "city") ?? string.Empty },
                    { "mailingState", GetStringValue(mailingAddress, "state") ?? string.Empty },
                    { "mailingZip", GetStringValue(mailingAddress, "zipCode") ?? string.Empty }
                },
                County = GetStringValue(formData, "county"),
                TradeName = GetStringValue(formData, "tradeName"),
                CareOfName = GetStringValue(formData, "careOfName"),
                ClosingMonth = closingMonth,
                FilingRequirement = GetStringValue(formData, "filingRequirement"),
                EmployeeDetails = employeeDetails.Any() ? JsonSerializer.Deserialize<EmployeeDetails>(JsonSerializer.Serialize(employeeDetails)) : null,
                ThirdPartyDesignee = thirdParty.Any() ? new ThirdPartyDesignee
                {
                    Name = GetStringValue(thirdParty, "name"),
                    Phone = GetStringValue(thirdParty, "phone"),
                    Fax = GetStringValue(thirdParty, "fax"),
                    Authorized = GetStringValue(thirdParty, "authorized")
                } : null,
                LlcDetails = llcDetails.ContainsKey("numberOfMembers") && llcDetails["numberOfMembers"] != null 
                    ? new LlcDetails { NumberOfMembers = llcDetails["numberOfMembers"]?.ToString() } 
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map form automation data");
            throw;
        }
    }

    private static string? GetStringValue(IDictionary<string, object>? dict, string? key)
    {
        if (dict == null || string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (dict.TryGetValue(key, out var value) && value != null)
        {
            return value.ToString();
        }
        return null;
    }

    private static IDictionary<string, object>? GetDictionaryValue(IDictionary<string, object>? dict, string? key)
    {
        if (dict == null || string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (dict.TryGetValue(key, out var value) && value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonElement.GetRawText());
        }
        return null;
    }

    private static List<Dictionary<string, object>>? GetListValue(IDictionary<string, object>? dict, string? key)
    {
        if (dict == null || string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (dict.TryGetValue(key, out var value) && value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonElement.GetRawText());
        }
        return null;
    }
}