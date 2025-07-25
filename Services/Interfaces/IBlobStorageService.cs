using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EinAutomation.Api.Models;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IBlobStorageService
    {
        /// <summary>
        /// Uploads bytes directly to Azure Blob Storage with content type and tags
        /// </summary>
        /// <param name="dataBytes">The data to upload</param>
        /// <param name="blobName">The blob name/path</param>
        /// <param name="contentType">The content type</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The URL of the uploaded blob</returns>
        Task<string> UploadBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads bytes directly to Azure Blob Storage with content type and tags
        /// </summary>
        /// <param name="dataBytes">The data to upload</param>
        /// <param name="blobName">The blob name/path</param>
        /// <param name="contentType">The content type</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The URL of the uploaded blob</returns>
        Task<string> UploadFinalBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads log files to Azure Blob Storage
        /// </summary>
        /// <param name="recordId">The associated record ID</param>
        /// <param name="logFilePath">Path to the log file</param>
        /// <returns>The URL of the uploaded log file or null if failed</returns>
        Task<string?> UploadLogToBlob(string? recordId, string? logFilePath);

        /// <summary>
        /// Saves JSON data to Azure Blob Storage synchronously
        /// </summary>
        /// <param name="data">The data to save as dictionary</param>
        /// <param name="caseData">Optional case data for naming</param>
        /// <param name="fileName">Optional custom file name</param>
        /// <returns>True if successful</returns>
        Task<bool> SaveJsonDataSync(Dictionary<string, object> data, CaseData? caseData = null, string? fileName = null);

        /// <summary>
        /// Uploads bytes to Azure Blob Storage and returns the URL.
        /// </summary>
        /// <param name="bytes">The byte array to upload</param>
        /// <param name="blobName">The name of the blob</param>
        /// <param name="contentType">The MIME type</param>
        /// <param name="overwrite">Whether to overwrite if blob exists</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The URL of the uploaded blob</returns>
        Task<string> UploadAsync(byte[] bytes, string blobName, string contentType, bool overwrite = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a dictionary as a JSON blob to Azure Storage.
        /// </summary>
        /// <param name="data">The data to serialize and upload</param>
        /// <param name="blobName">The blob path (including folder and file name)</param>
        /// <param name="contentType">Optional content type, defaults to "application/json"</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if upload succeeds</returns>
        Task<bool> UploadJsonAsync(Dictionary<string, object> data, string blobName, string? contentType = "application/json", CancellationToken cancellationToken = default);
    }
}