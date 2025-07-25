// ValidationExtensions.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;

namespace EinAutomation.Api.Models;

public static class ValidationExtensions
{
    public static ValidationResult? Validate(this CaseData caseData)
    {
        var context = new ValidationContext(caseData);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(caseData, context, results, true);

        // Required fields
        if (string.IsNullOrWhiteSpace(caseData.EntityName))
            results.Add(new ValidationResult("EntityName is required.", new[] { nameof(caseData.EntityName) }));

        if (string.IsNullOrWhiteSpace(caseData.EntityType))
            results.Add(new ValidationResult("EntityType is required.", new[] { nameof(caseData.EntityType) }));

        // Validate SSN format
        if (!string.IsNullOrWhiteSpace(caseData.SsnDecrypted) &&
            !Regex.IsMatch(caseData.SsnDecrypted, @"^\d{9}$|^\d{2}-\d{7}$|^\d{3}-\d{2}-\d{4}$"))
            results.Add(new ValidationResult("SsnDecrypted must be a valid 9-digit number, SSN, ITIN, or EIN format.", new[] { nameof(caseData.SsnDecrypted) }));

        // Validate physical address (business_address_1, city, entity_state, zip_code)
        if (string.IsNullOrWhiteSpace(caseData.BusinessAddress1))
            results.Add(new ValidationResult("BusinessAddress1 is required.", new[] { nameof(caseData.BusinessAddress1) }));

        if (string.IsNullOrWhiteSpace(caseData.City))
            results.Add(new ValidationResult("City is required.", new[] { nameof(caseData.City) }));

        if (string.IsNullOrWhiteSpace(caseData.EntityState) || !Regex.IsMatch(caseData.EntityState, @"^[A-Z]{2}$"))
            results.Add(new ValidationResult("EntityState must be a valid 2-letter code.", new[] { nameof(caseData.EntityState) }));

        if (string.IsNullOrWhiteSpace(caseData.ZipCode) || !Regex.IsMatch(caseData.ZipCode, @"^\d{5}(-\d{4})?$"))
            results.Add(new ValidationResult("ZipCode must be a valid 5-digit or 9-digit format.", new[] { nameof(caseData.ZipCode) }));

        // Validate mailing address if provided
        if (caseData.MailingAddress != null && caseData.MailingAddress.Any())
        {
            var mailing = caseData.MailingAddress.FirstOrDefault();
            if (mailing == null)
            {
                results.Add(new ValidationResult("MailingAddress is empty.", new[] { nameof(caseData.MailingAddress) }));
            }
            else
            {
                if (!mailing.ContainsKey("mailingStreet") || string.IsNullOrWhiteSpace(mailing["mailingStreet"]))
                    results.Add(new ValidationResult("MailingStreet is required when MailingAddress is provided.", new[] { nameof(caseData.MailingAddress) }));

                if (!mailing.ContainsKey("mailingCity") || string.IsNullOrWhiteSpace(mailing["mailingCity"]))
                    results.Add(new ValidationResult("MailingCity is required when MailingAddress is provided.", new[] { nameof(caseData.MailingAddress) }));

                if (!mailing.ContainsKey("mailingState") || string.IsNullOrWhiteSpace(mailing["mailingState"]) ||
                    !Regex.IsMatch(mailing["mailingState"], @"^[A-Z]{2}$"))
                    results.Add(new ValidationResult("MailingState must be a valid 2-letter code.", new[] { nameof(caseData.MailingAddress) }));

                if (!mailing.ContainsKey("mailingZip") || string.IsNullOrWhiteSpace(mailing["mailingZip"]) ||
                    !Regex.IsMatch(mailing["mailingZip"], @"^\d{5}(-\d{4})?$"))
                    results.Add(new ValidationResult("MailingZip must be a valid 5-digit or 9-digit format.", new[] { nameof(caseData.MailingAddress) }));
            }
        }

        // Validate entity members if provided
        if (caseData.EntityMembers != null && caseData.EntityMembers.Any())
        {
            if (!caseData.EntityMembers.ContainsKey("first_name_1") || string.IsNullOrWhiteSpace(caseData.EntityMembers["first_name_1"]))
                if (!caseData.EntityMembers.ContainsKey("last_name_1") || string.IsNullOrWhiteSpace(caseData.EntityMembers["last_name_1"]))
                    results.Add(new ValidationResult("At least one of first_name_1 or last_name_1 is required in EntityMembers.", new[] { nameof(caseData.EntityMembers) }));

            if (caseData.EntityMembers.ContainsKey("phone_1") && !string.IsNullOrWhiteSpace(caseData.EntityMembers["phone_1"]) &&
                !Regex.IsMatch(caseData.EntityMembers["phone_1"], @"^\d{10}$"))
                results.Add(new ValidationResult("phone_1 must be a valid 10-digit phone number.", new[] { nameof(caseData.EntityMembers) }));
        }

        // Validate LLC details if provided
        if (caseData.LlcDetails != null)
        {
            var llcDetailsResult = caseData.LlcDetails.Validate();
            if (llcDetailsResult != null) results.Add(llcDetailsResult);
        }

        // Validate employee details if provided
        if (caseData.EmployeeDetails != null)
        {
            var employeeDetailsResult = caseData.EmployeeDetails.Validate();
            if (employeeDetailsResult != null) results.Add(employeeDetailsResult);
        }

        // Validate third party designee if provided
        if (caseData.ThirdPartyDesignee != null)
        {
            var thirdPartyResult = caseData.ThirdPartyDesignee.Validate();
            if (thirdPartyResult != null) results.Add(thirdPartyResult);
        }

        return results.FirstOrDefault();
    }

    public static ValidationResult? Validate(this ThirdPartyDesignee thirdPartyDesignee)
    {
        var context = new ValidationContext(thirdPartyDesignee);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(thirdPartyDesignee, context, results, true);

        if (!string.IsNullOrWhiteSpace(thirdPartyDesignee.Phone) &&
            !Regex.IsMatch(thirdPartyDesignee.Phone, @"^\d{10}$"))
            results.Add(new ValidationResult("Phone must be a valid 10-digit number.", new[] { nameof(thirdPartyDesignee.Phone) }));

        if (!string.IsNullOrWhiteSpace(thirdPartyDesignee.Fax) &&
            !Regex.IsMatch(thirdPartyDesignee.Fax, @"^\d{10}$"))
            results.Add(new ValidationResult("Fax must be a valid 10-digit number.", new[] { nameof(thirdPartyDesignee.Fax) }));

        return results.FirstOrDefault();
    }

    public static ValidationResult? Validate(this EmployeeDetails employeeDetails)
    {
        var context = new ValidationContext(employeeDetails);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(employeeDetails, context, results, true);

        // Optional: Add custom validation for Other field if needed
        return results.FirstOrDefault();
    }

    public static ValidationResult? Validate(this LlcDetails llcDetails)
    {
        var context = new ValidationContext(llcDetails);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(llcDetails, context, results, true);

        if (!string.IsNullOrWhiteSpace(llcDetails.NumberOfMembers) && (!int.TryParse(llcDetails.NumberOfMembers, out int num) || num <= 0))
            results.Add(new ValidationResult("NumberOfMembers must be a positive integer.", new[] { nameof(llcDetails.NumberOfMembers) }));

        return results.FirstOrDefault();
    }
}