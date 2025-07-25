using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

#nullable enable

namespace EinAutomation.Api.Infrastructure
{
    public static class KeyVaultConfigurationExtensions
    {
        /// <summary>
        /// Adds Azure Key Vault secrets as an in-memory configuration source.
        /// Loads all secrets and maps select secrets to strongly-typed config paths.
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder</param>
        /// <param name="keyVaultName">Optional Key Vault name override</param>
        public static void AddKeyVaultSecrets(this IConfigurationBuilder? configurationBuilder, string? keyVaultName = null)
        {
            if (configurationBuilder == null)
                throw new ArgumentNullException(nameof(configurationBuilder));

            try
            {
                // If not provided, use default hardcoded Key Vault name
                keyVaultName ??= "corpnet-formpal-vault";
                var kvUri = $"https://{keyVaultName}.vault.azure.net";

                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    AdditionallyAllowedTenants = { "4029e427-9d7e-479f-bac5-2eb798a78622" }
                });

                var secretClient = new SecretClient(new Uri(kvUri), credential);
                var inMemoryConfig = new Dictionary<string, string?>();

                // Fetch all secrets
                foreach (var secretProperties in secretClient.GetPropertiesOfSecrets())
                {
                    try
                    {
                        var secret = secretClient.GetSecret(secretProperties.Name);
                        inMemoryConfig[secretProperties.Name] = secret.Value.Value;
                    }
                    catch (Exception ex)
                    {
                        throw new ApplicationException($"Failed to load secret '{secretProperties.Name}' from Key Vault", ex);
                    }
                }

                // Map flat secrets to structured config keys
                if (inMemoryConfig.TryGetValue("SALESFORCE-CLIENT-ID", out var salesforceClientId))
                    inMemoryConfig["Salesforce:ClientId"] = salesforceClientId;

                if (inMemoryConfig.TryGetValue("SALESFORCE-CLIENT-SECRET", out var salesforceClientSecret))
                    inMemoryConfig["Salesforce:ClientSecret"] = salesforceClientSecret;

                if (inMemoryConfig.TryGetValue("SALESFORCE-USERNAME", out var salesforceUsername))
                    inMemoryConfig["Salesforce:Username"] = salesforceUsername;

                if (inMemoryConfig.TryGetValue("SALESFORCE-PASSWORD", out var salesforcePassword))
                    inMemoryConfig["Salesforce:Password"] = salesforcePassword;

                if (inMemoryConfig.TryGetValue("SALESFORCE-TOKEN", out var salesforceToken))
                    inMemoryConfig["Salesforce:Token"] = salesforceToken;

                if (inMemoryConfig.TryGetValue("TENANT-ID", out var tenantId))
                    inMemoryConfig["AzureAd:TenantId"] = tenantId;

                if (inMemoryConfig.TryGetValue("CLIENT-ID", out var clientId))
                {
                    inMemoryConfig["AzureAd:ClientId"] = clientId;
                    inMemoryConfig["KeyVault:ManagedIdentityClientId"] = clientId;
                }

                if (inMemoryConfig.TryGetValue("AZURE-BLOB-CONNECTION-STRING", out var blobConnStr))
                    inMemoryConfig["Azure:Blob:ConnectionString"] = blobConnStr;

                // Inject mapped secrets into configuration
                configurationBuilder.AddInMemoryCollection(inMemoryConfig);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed to initialize Key Vault configuration", ex);
            }
        }
    }
}
