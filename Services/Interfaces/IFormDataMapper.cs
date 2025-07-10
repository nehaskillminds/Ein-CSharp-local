// IFormDataMapper.cs
using EinAutomation.Api.Models;
using System.Collections.Generic;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IFormDataMapper
    {
        CaseData MapFormAutomationData(IDictionary<string, object> formData);
    }
}