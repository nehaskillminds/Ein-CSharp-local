using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

#nullable enable

namespace EinAutomation.Api.Infrastructure
{
    public static class KeyVaultConfigurationExtensions
    {
        /// <summary>
        /// Adds Azure Key Vault as a configuration source with support for required and optional secrets
        /// </summary>
        /// <param name="configurationBuilder">The configuration builder</param>
        /// <param name="keyVaultName">Optional override for the Key Vault name</param>
        public static void AddKeyVaultSecrets(this IConfigurationBuilder? configurationBuilder, string? keyVaultName = null)
        {
            if (configurationBuilder == null)
            {
                throw new ArgumentNullException(nameof(configurationBuilder));
            }

            var tempConfig = configurationBuilder.Build();

            keyVaultName ??= tempConfig["KeyVault:Name"] ?? "corpnet-formpal-keyvault";
            var kvUri = $"https://{keyVaultName}.vault.azure.net";

            try
            {
                var credential = new DefaultAzureCredential();
                var secretClient = new SecretClient(new Uri(kvUri), credential);

                var requiredConfigs = new Dictionary<string, string?>
                {
                    {"Salesforce:ClientId", null},
                    {"Salesforce:ClientSecret", null},
                    {"Salesforce:Username", null},
                    {"Salesforce:Password", null},
                    {"Salesforce:Token", null},
                    {"AzureAd:TenantId", null},
                    {"AzureAd:ClientId", null},
                    {"Azure:Blob:ConnectionString", null}
                };

                var optionalConfigs = new Dictionary<string, string?>
                {
                    {"KeyVault:ManagedIdentityClientId", null}
                };

                var inMemoryConfig = new Dictionary<string, string?>();

                foreach (var configItem in requiredConfigs)
                {
                    string secretKey = configItem.Key;
                    try
                    {
                        KeyVaultSecret? secret = secretClient.GetSecret(secretKey);
                        inMemoryConfig.Add(secretKey, secret?.Value);
                    }
                    catch (Exception ex)
                    {
                        throw new ApplicationException($"Failed to load required secret '{secretKey}' from Key Vault", ex);
                    }
                }

                foreach (var configItem in optionalConfigs)
                {
                    try
                    {
                        KeyVaultSecret? secret = secretClient.GetSecret(configItem.Key);
                        inMemoryConfig.Add(configItem.Key, secret?.Value);
                    }
                    catch
                    {
                        inMemoryConfig.Add(configItem.Key, configItem.Value);
                    }
                }

                configurationBuilder.AddInMemoryCollection(inMemoryConfig);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed to initialize Key Vault configuration for vault '{kvUri}'", ex);
            }
        }
    }
}