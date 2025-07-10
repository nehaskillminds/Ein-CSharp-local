// IAutomationOrchestrator.cs
using EinAutomation.Api.Models;
using System.Threading;
using System.Threading.Tasks;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IAutomationOrchestrator
    {
        /// <summary>
        /// Runs the EIN automation process, including form filling, PDF uploads, and Salesforce notifications.
        /// </summary>
        /// <param name="data">The CaseData object containing form input data.</param>
        /// <param name="ct">Cancellation token for async operation cancellation.</param>
        /// <returns>A tuple containing:
        ///   - Success: A boolean indicating if the automation was successful.
        ///   - EinNumber: The EIN number if successful, or an error message if failed.
        ///   - AzureBlobUrl: The URL of the uploaded EIN letter or confirmation PDF, or null if no PDF was uploaded.
        /// </returns>
        Task<(bool Success, string EinNumber, string? AzureBlobUrl)> RunAsync(CaseData data, CancellationToken ct);
    }
}