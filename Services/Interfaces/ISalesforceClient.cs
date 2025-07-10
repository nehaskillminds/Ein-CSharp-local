// ISalesforceClient.cs
using System.Threading;
using System.Threading.Tasks;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface ISalesforceClient
    {
        Task<bool> InitializeSalesforceAuthAsync();

        Task<bool> NotifySalesforceSuccessAsync(string entityProcessId, string einNumber);
        Task<bool> NotifySalesforceErrorCodeAsync(string entityProcessId, string errorCode, string? status = "fail");
        Task<bool> NotifyScreenshotUploadToSalesforceAsync(string entityProcessId, string? blobUrl, string entityName, string docType);
        Task<bool> NotifyEinLetterToSalesforceAsync(string entityProcessId, string? blobUrl, string entityName);
        Task<bool> NotifyConfirmationAsync(string entityProcessId, string? blobUrl, string entityName);

        // ✅ Required for AutomationOrchestrator
        Task NotifyContentMigrationAsync(string entityProcessId, string? blobUrl, string fileName, string docType, bool hiddenFromClient, CancellationToken ct);
        Task UpdateEinStatusAsync(string entityProcessId, string status, string? einNumber, string? errorCode, CancellationToken ct);

        bool IsAuthenticated { get; }
        string? InstanceUrl { get; }
    }
}