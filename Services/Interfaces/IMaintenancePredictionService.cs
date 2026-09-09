using System.Collections.Generic;
using System.Threading.Tasks;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;

namespace Fleetify.Services.Interfaces
{
    public interface IMaintenancePredictionService
    {
        MaintenancePredictionResult EvaluateVehicle(Vehicle vehicle);
        Task<MaintenanceAlert?> CheckAndGenerateAlertAsync(int vehicleId);
        Task<List<MaintenanceAlert>> EvaluateAllVehiclesAsync();
    }
}
