using System.Threading.Tasks;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;

namespace Fleetify.Services.Interfaces
{
    public interface ICostEstimationService
    {
        CostEstimateResult CalculateCost(CostEstimateRequest request);
        Task<CostEstimateResult> CalculateCostAsync(CostEstimateRequest request);
        Task<CostEstimate> SaveCostEstimateAsync(int deliveryRequestId, CostEstimateRequest request, CostEstimateResult result);
    }
}
