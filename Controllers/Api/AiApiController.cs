using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Controllers.Api
{
    [ApiController]
    [Route("api/ai")]
    public class AiApiController : ControllerBase
    {
        private readonly ICostEstimationService _costService;
        private readonly IMaintenancePredictionService _maintenanceService;

        public AiApiController(
            ICostEstimationService costService,
            IMaintenancePredictionService maintenanceService)
        {
            _costService = costService;
            _maintenanceService = maintenanceService;
        }

        // POST: /api/ai/estimate-cost
        [HttpPost("estimate-cost")]
        public IActionResult EstimateCost([FromBody] CostEstimateRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Invalid cost estimation request data." });
            }

            var result = _costService.CalculateCost(request);
            return Ok(result);
        }

        // POST: /api/ai/predict-maintenance/{vehicleId}
        [HttpPost("predict-maintenance/{vehicleId}")]
        public async Task<IActionResult> PredictMaintenance(int vehicleId)
        {
            var alert = await _maintenanceService.CheckAndGenerateAlertAsync(vehicleId);
            return Ok(new { success = true, alertGenerated = alert != null, alert });
        }
    }
}
