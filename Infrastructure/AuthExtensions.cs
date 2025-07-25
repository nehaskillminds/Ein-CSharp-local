using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

#nullable enable

namespace EinAutomation.Api.Infrastructure
{
    public static class AzureAdAuthenticationExtensions
    {
        public static async Task<ClaimsPrincipal?> GetCurrentUserAsync(
            HttpContext? context,
            IConfiguration? configuration,
            string? token = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // Get token from Authorization header if not provided
            token ??= GetTokenFromHeader(context);

            if (string.IsNullOrEmpty(token))
            {
                throw new UnauthorizedAccessException("No authorization token was found");
            }

            try
            {
                var tenantId = configuration["TENANT-ID"] ??
                    throw new InvalidOperationException("TENANT-ID configuration is missing");
                var clientId = configuration["CLIENT-ID"] ??
                    throw new InvalidOperationException("CLIENT-ID configuration is missing");

                var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever());

                var openIdConfig = await configManager.GetConfigurationAsync(CancellationToken.None);

                var tokenHandler = new JwtSecurityTokenHandler();
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidAudiences = new[] { $"api://{clientId}", clientId },
                    ValidIssuers = new[] { $"https://sts.windows.net/{tenantId}/" },
                    IssuerSigningKeys = openIdConfig?.SigningKeys,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                }, out SecurityToken? validatedToken);

                // Check for required claims
                if (principal == null || !principal.HasClaim(c => c.Type == ClaimTypes.NameIdentifier))
                {
                    throw new SecurityTokenValidationException("Token is missing required claims");
                }

                return principal;
            }
            catch (SecurityTokenException ex)
            {
                throw new UnauthorizedAccessException("Invalid or expired token", ex);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException("Failed to validate token", ex);
            }
        }

        private static string? GetTokenFromHeader(HttpContext context)
        {
            string? authorization = context.Request?.Headers["Authorization"];

            if (string.IsNullOrEmpty(authorization))
            {
                return null;
            }

            if (authorization.StartsWith(JwtBearerDefaults.AuthenticationScheme + " ", StringComparison.OrdinalIgnoreCase))
            {
                return authorization.Substring(JwtBearerDefaults.AuthenticationScheme.Length + 1).Trim();
            }

            return null;
        }

        // Extension method for easy use in controllers
        public static async Task<ClaimsPrincipal?> GetCurrentUserAsync(this HttpContext? context, IConfiguration? configuration)
        {
            return await GetCurrentUserAsync(context, configuration, null);
        }
    }
}