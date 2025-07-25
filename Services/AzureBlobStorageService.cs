using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Configuration;

#nullable enable

namespace EinAutomation.Api.Services
{
    public class AzureBlobStorageService : IBlobStorageService
    {
        private readonly ILogger<AzureBlobStorageService> _logger;
        private readonly string _connectionString;
        private readonly string _containerName;

        public AzureBlobStorageService(
            ILogger<AzureBlobStorageService> logger,
            IOptions<EinAutomation.Api.Options.AzureBlobStorageOptions> options,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Try to get connection string from configuration (Key Vault or appsettings)
            _connectionString = configuration["Azure:Blob:ConnectionString"];

            // If not found, try environment variable (e.g., set in Dockerfile)
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _logger.LogWarning("Azure:Blob:ConnectionString not found in configuration, checking environment variable...");

                _connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

                if (string.IsNullOrWhiteSpace(_connectionString))
                {
                    _logger.LogWarning("AZURE_STORAGE_CONNECTION_STRING not found in environment, using hardcoded fallback.");
                    _connectionString = "";
                }
                else
                {
                    _logger.LogInformation("Using Azure Blob connection string from environment variable.");
                }
            }
            else
            {
                _logger.LogInformation("Using Azure Blob connection string from configuration.");
            }

            // Hardcoded container name for testing only
            _containerName = "corpnetcrmdocs";
            _logger.LogInformation("AzureBlobStorageService: ConnectionString={{ConnectionString}}, Container={{Container}}", _connectionString, _containerName);
        }

        public async Task<string> UploadBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
            if (dataBytes == null)
                throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null)
                throw new ArgumentNullException(nameof(blobName));
            if (contentType == null)
                throw new ArgumentNullException(nameof(contentType));

            try
            {
                BlobServiceClient blobServiceClient = new(_connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

                // Upload the blob, overwriting if it exists
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                // Set content type separately after upload
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                });

                // Set blob tags
                var tags = new Dictionary<string, string>
                {
                    { "HiddenFromClient", "true" }
                };
                await blobClient.SetTagsAsync(tags);

                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("Uploaded to Azure Blob Storage with tags: {BlobUrl}", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task<string> UploadFinalBytesToBlob(byte[] dataBytes, string blobName, string contentType, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
            if (dataBytes == null)
                throw new ArgumentNullException(nameof(dataBytes));
            if (blobName == null)
                throw new ArgumentNullException(nameof(blobName));
            if (contentType == null)
                throw new ArgumentNullException(nameof(contentType));

            try
            {
                BlobServiceClient blobServiceClient = new(_connectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

                // Upload the blob, overwriting if it exists
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                using (var stream = new MemoryStream(dataBytes))
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
                }

                // Set content type separately after upload
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
                {
                    ContentType = contentType
                });

                
                string blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("Uploaded to Azure Blob Storage with tags: {BlobUrl}", blobUrl);
                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure upload failed for blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task<string?> UploadLogToBlob(string? recordId, string? logFilePath)
        {
            string blobName = $"logs/{recordId}/chromedriver_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.log";
            _logger.LogInformation("Uploading blob: {BlobName}", blobName);
            if (string.IsNullOrEmpty(recordId) || string.IsNullOrEmpty(logFilePath))
            {
                _logger.LogWarning("Invalid input parameters for UploadLogToBlob: recordId or logFilePath is null or empty");
                return null;
            }

            try
            {
                if (!File.Exists(logFilePath))
                {
                    _logger.LogWarning("No log file found at {LogFilePath}", logFilePath);
                    return null;
                }

                byte[] logData = await File.ReadAllBytesAsync(logFilePath);
                return await UploadBytesToBlob(logData, blobName, "text/plain");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload logs to Azure Blob for record ID {RecordId} from {LogFilePath}", recordId, logFilePath);
                return null;
            }
        }

public async Task<bool> UploadJsonAsync(Dictionary<string, object> data, string blobName, string? contentType = "application/json", CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Uploading blob: {BlobName}", blobName);
    if (data == null)
        throw new ArgumentNullException(nameof(data));
    if (blobName == null)
        throw new ArgumentNullException(nameof(blobName));

    try
    {
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        BlobServiceClient blobServiceClient = new(_connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);

        // Use a longer timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cts.Token);

        BlobClient blobClient = containerClient.GetBlobClient(blobName);
        using (var stream = new MemoryStream(bytes))
        {
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cts.Token);
        }

        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = contentType ?? "application/json" }, cancellationToken: cts.Token);
        await blobClient.SetTagsAsync(new Dictionary<string, string> { { "HiddenFromClient", "true" } }, cancellationToken: cts.Token);

        _logger.LogInformation("JSON blob uploaded to {BlobName}", blobName);
        return true;
    }
    catch (TaskCanceledException ex)
    {
        _logger.LogWarning(ex, "Upload operation was canceled for blob {BlobName}", blobName);
        return false;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload JSON blob {BlobName}", blobName);
        return false;
    }
}
public async Task<bool> SaveJsonDataSync(Dictionary<string, object> data, CaseData? caseData = null, string? fileName = null)
{
    if (data == null)
        throw new ArgumentNullException(nameof(data));
    if (!data.ContainsKey("record_id"))
    {
        _logger.LogWarning("Invalid input parameters for SaveJsonDataSync: data does not contain record_id");
        return false;
    }
    try
    {
        string legalName = data.ContainsKey("entity_name") ? data["entity_name"]?.ToString() ?? "UnknownEntity" : "UnknownEntity";
        string cleanLegalName = Regex.Replace(legalName, @"[^\w]", "");
        // If no custom file name, default to EntityName_data.json
        fileName ??= $"{cleanLegalName}_data.json";
        string blobName = $"EntityProcess/{data["record_id"]}/{fileName}";
        _logger.LogInformation("Uploading blob: {BlobName}", blobName);
        string jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        byte[] dataBytes = Encoding.UTF8.GetBytes(jsonData);
        BlobServiceClient blobServiceClient = new(_connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobName);
        // Try upload first, create container only if needed
        await UploadWithContainerCreation(blobClient, new MemoryStream(dataBytes), containerClient, CancellationToken.None);
        // Set content type separately after upload
        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders
        {
            ContentType = "application/json"
        });
        // Set blob tags
        var tags = new Dictionary<string, string>
        {
            { "HiddenFromClient", "true" }
        };
        await blobClient.SetTagsAsync(tags);
        string blobUrl = blobClient.Uri.AbsoluteUri;
        _logger.LogInformation("JSON data uploaded with tags: {BlobUrl}", blobUrl);
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload JSON data to Azure Blob for filename {FileName}", fileName);
        return false;
    }
}
public async Task<string> UploadAsync(byte[] bytes, string blobName, string contentType, bool overwrite = true, CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Uploading blob: {BlobName}", blobName);
    if (bytes == null)
        throw new ArgumentNullException(nameof(bytes));
    if (blobName == null)
        throw new ArgumentNullException(nameof(blobName));
    if (contentType == null)
        throw new ArgumentNullException(nameof(contentType));
    try
    {
        BlobServiceClient blobServiceClient = new(_connectionString);
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
        BlobClient blobClient = containerClient.GetBlobClient(blobName);
        // Try upload first, create container only if needed
        await UploadWithContainerCreation(blobClient, new MemoryStream(bytes), containerClient, cancellationToken, overwrite);
        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
        _logger.LogInformation("Uploaded blob '{BlobName}' with content type '{ContentType}'", blobName, contentType);
        return blobClient.Uri.AbsoluteUri;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "UploadAsync failed for blob {BlobName}", blobName);
        throw;
    }
}
// Helper method to handle upload with container creation
private async Task UploadWithContainerCreation(BlobClient blobClient, MemoryStream stream, BlobContainerClient containerClient, CancellationToken cancellationToken, bool overwrite = true)
{
    try
    {
        await blobClient.UploadAsync(stream, overwrite: overwrite, cancellationToken: cancellationToken);
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        // Container doesn't exist, try to create it once
        _logger.LogInformation("Container does not exist. Creating container: {ContainerName}", containerClient.Name);
        try
        {
            await containerClient.CreateAsync(PublicAccessType.None, cancellationToken: CancellationToken.None);
        }
        catch (RequestFailedException createEx) when (createEx.Status == 409)
        {
            // Container already exists (race condition), ignore
            _logger.LogDebug("Container already exists (race condition)");
        }
        // Reset stream position and retry the upload
        stream.Position = 0;
        await blobClient.UploadAsync(stream, overwrite: overwrite, cancellationToken: cancellationToken);
    }
    finally
    {
        stream?.Dispose();
    }
}
    }
}



