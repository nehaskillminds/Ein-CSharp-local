using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EinAutomation.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EinController : ControllerBase
    {
        private readonly IAutomationOrchestrator _orchestrator;
        private readonly IFormDataMapper _formDataMapper; // Assuming FormDataMapper is also interfaced
        private readonly ILogger<EinController> _logger;

        public EinController(
            IAutomationOrchestrator orchestrator,
            IFormDataMapper formDataMapper,
            ILogger<EinController> logger)
        {
            _orchestrator = orchestrator;
            _formDataMapper = formDataMapper;
            _logger = logger;
        }

        [HttpPost("run-irs-ein")]
        // [Authorize]
        public async Task<IActionResult> RunIrsEinAsync([FromBody] JsonElement request, CancellationToken ct)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(request.GetRawText());
                if (data == null || !data.ContainsKey("entityProcessId") || data["formType"]?.ToString() != "EIN")
                {
                    _logger.LogError("Invalid payload or formType");
                    return BadRequest("Invalid payload or formType");
                }

                //var caseData = await _formDataMapper.MapFormAutomationDataAsync(data);
                var caseData = _formDataMapper.MapFormAutomationData(data);
                var (success, einNumber, azureBlobUrl) = await _orchestrator.RunAsync(caseData, ct);

                if (success)
                {
                    return Ok(new
                    {
                        Message = "Form submitted successfully",
                        Status = "Submitted",
                        RecordId = caseData.RecordId,
                        AzureBlobUrl = azureBlobUrl
                    });
                }

                return BadRequest(einNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Endpoint error");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}