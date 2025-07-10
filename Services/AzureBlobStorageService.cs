using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Configuration;
using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using EinAutomation.Api.Options;



namespace EinAutomation.Api.Services
{
    public class AzureBlobStorageService : IBlobStorageService
    {
        private readonly ILogger<AzureBlobStorageService> _logger;
        private readonly string _connectionString;
        private readonly string _containerName;

        public AzureBlobStorageService(
            ILogger<AzureBlobStorageService> logger,
            IOptions<AzureBlobStorageOptions> options,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (options?.Value == null)
                throw new ArgumentNullException(nameof(options), "Azure Blob Storage options are not configured.");

            _containerName = options.Value.Container ?? throw new ArgumentNullException(nameof(options.Value.Container), "Azure Blob Storage Container name is not configured.");

            // Get storage account credentials from configuration
            var accountName = configuration["Azure:Storage:AccountName"] 
                ?? throw new ArgumentException("Azure Storage AccountName is not configured");
            var accountKey = configuration["Azure:Storage:AccountKey"] 
                ?? throw new ArgumentException("Azure Storage AccountKey is not configured");
            var endpointSuffix = configuration["Azure:Storage:EndpointSuffix"] ?? "core.windows.net";

            // Construct connection string
            _connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix={endpointSuffix}";
            
            _logger.LogInformation("Using dynamically constructed Azure Blob Storage connection string");
        }

        public async Task<string> UploadBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default)
        {
            if (dataBytes == null) throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null) throw new ArgumentNullException(nameof(blobName));
            if (contentType == null) throw new ArgumentNullException(nameof(contentType));

            try
            {
                var blobServiceClient = new BlobServiceClient(_connectionString, new BlobClientOptions
                {
                    Retry = { MaxRetries = 3, Delay = TimeSpan.FromSeconds(5) }
                });
                
                var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

                var blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                }, cancellationToken: cancellationToken);

                await blobClient.SetTagsAsync(new Dictionary<string, string>
                {
                    { "HiddenFromClient", "true" }
                }, cancellationToken: cancellationToken);

                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("Uploaded to Azure Blob Storage: {BlobUrl}", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task<string?> UploadLogToBlob(string? recordId, string? logFilePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(recordId) || string.IsNullOrEmpty(logFilePath))
            {
                _logger.LogWarning("Invalid input parameters for UploadLogToBlob");
                return null;
            }

            try
            {
                if (!File.Exists(logFilePath))
                {
                    _logger.LogWarning("Log file not found at {LogFilePath}", logFilePath);
                    return null;
                }

                byte[] logData = await File.ReadAllBytesAsync(logFilePath, cancellationToken);
                string blobName = $"logs/{recordId}/chromedriver_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.log";
                return await UploadBytesToBlob(logData, blobName, "text/plain", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload logs for record ID {RecordId}", recordId);
                return null;
            }
        }

        public async Task<bool> SaveJsonDataSync(Dictionary<string, object> data, CaseData? caseData = null, string? fileName = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            
            if (!data.ContainsKey("record_id"))
            {
                _logger.LogWarning("Data does not contain record_id");
                return false;
            }

            try
            {
                string legalName = data.ContainsKey("entity_name") ? data["entity_name"]?.ToString() ?? "UnknownEntity" : "UnknownEntity";
                string cleanLegalName = Regex.Replace(legalName, @"[^\w]", "");

                fileName ??= $"{cleanLegalName}_data.json";
                string blobName = $"EntityProcess/{data["record_id"]}/{fileName}";

                return await UploadJsonAsync(data, blobName, "application/json", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload JSON data");
                return false;
            }
        }

        public async Task<string> UploadAsync(byte[] bytes, string blobName, string contentType, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (blobName == null) throw new ArgumentNullException(nameof(blobName));
            if (contentType == null) throw new ArgumentNullException(nameof(contentType));

            try
            {
                var blobServiceClient = new BlobServiceClient(_connectionString, new BlobClientOptions
                {
                    Retry = { MaxRetries = 3, Delay = TimeSpan.FromSeconds(5) }
                });
                
                var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

                var blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(bytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: overwrite, cancellationToken: cancellationToken);
                    await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
                }

                _logger.LogInformation("Uploaded blob {BlobName}", blobName);
                return blobClient.Uri.AbsoluteUri;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed for blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task<bool> UploadJsonAsync(Dictionary<string, object> data, string blobName, string? contentType = "application/json", CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (blobName == null) throw new ArgumentNullException(nameof(blobName));

            try
            {
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                var blobServiceClient = new BlobServiceClient(_connectionString, new BlobClientOptions
                {
                    Retry = { MaxRetries = 3, Delay = TimeSpan.FromSeconds(5) }
                });
                
                var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

                var blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(bytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders 
                { 
                    ContentType = contentType ?? "application/json" 
                }, cancellationToken: cancellationToken);

                await blobClient.SetTagsAsync(new Dictionary<string, string> 
                { 
                    { "HiddenFromClient", "true" } 
                }, cancellationToken: cancellationToken);

                _logger.LogInformation("JSON uploaded to {BlobName}", blobName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload JSON to {BlobName}", blobName);
                return false;
            }
        }
    }
}