using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EinAutomation.Api.Infrastructure;
using EinAutomation.Api.Services;
using EinAutomation.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using EinAutomation.Api.Services.Interfaces; 

namespace EinAutomation.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllers();

            // Configure CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });

            // Configure authentication (Azure AD)
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
                options.Audience = $"api://{builder.Configuration["AzureAd:ClientId"]}";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"https://sts.windows.net/{builder.Configuration["AzureAd:TenantId"]}/",
                    ValidateAudience = true,
                    ValidAudience = $"api://{builder.Configuration["AzureAd:ClientId"]}"
                };
            });

            // This binds the "Azure:Blob" section from appsettings.json to AzureBlobStorageOptions
            builder.Services.Configure<AzureBlobStorageOptions>(builder.Configuration.GetSection("Azure:Blob"));

            // Register services
            builder.Services.AddSingleton<IEinFormFiller, IRSEinFormFiller>();
            builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
            builder.Services.AddSingleton<ISalesforceClient, SalesforceClient>();
            builder.Services.AddSingleton<IAutomationOrchestrator, AutomationOrchestrator>();
            builder.Services.AddSingleton<IFormDataMapper, FormDataMapper>();
            builder.Services.AddHttpClient<ISalesforceClient, SalesforceClient>();
            builder.Services.AddSingleton<IErrorMessageExtractionService, ErrorMessageExtractionService>();


            // Add configuration for Key Vault
            //builder.Services.AddKeyVaultConfiguration(builder.Configuration);
            builder.Configuration.AddKeyVaultSecrets();

            // Add logging
            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            });

            // Add Swagger for API documentation
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "IRS EIN API", Version = "v1", Description = "Automated IRS EIN form processing with Azure AD auth" });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowAll");
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}