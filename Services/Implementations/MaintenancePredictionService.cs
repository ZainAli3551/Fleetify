using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Fleetify.Data;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class MaintenancePredictionService : IMaintenancePredictionService
    {
        private readonly FleetifyDbContext _context;
        private readonly INotificationService _notificationService;

        public MaintenancePredictionService(FleetifyDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public MaintenancePredictionResult EvaluateVehicle(Vehicle vehicle)
        {
            double mileageSinceLastService = vehicle.CurrentMileage - vehicle.LastServiceMileage;
            if (mileageSinceLastService < 0) mileageSinceLastService = vehicle.CurrentMileage;

            var result = new MaintenancePredictionResult
            {
                VehicleId = vehicle.VehicleID,
                MileageSinceLastService = mileageSinceLastService
            };

            // Step 1: Analyze mileage intervals and condition status
            if (vehicle.ConditionStatus.Equals("Critical", StringComparison.OrdinalIgnoreCase) || mileageSinceLastService >= 12000)
            {
                result.AlertType = "Critical Maintenance";
                result.Severity = "Critical";
                result.RequiresImmediateAction = true;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} has exceeded {mileageSinceLastService:N0} km without full overhaul. Urgent brake, transmission, and engine overhaul required.";
                result.ActionItems.Add("Take vehicle off active assignments immediately");
                result.ActionItems.Add("Schedule complete engine diagnostic and fluid flush");
                result.ActionItems.Add("Inspect brake pads and rotor wear");
            }
            else if (vehicle.ConditionStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase) || mileageSinceLastService >= 7500)
            {
                result.AlertType = "Service Due";
                result.Severity = "High";
                result.RequiresImmediateAction = true;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} is due for scheduled 8,000 km oil, filter, and tire alignment service.";
                result.ActionItems.Add("Schedule oil and air filter change");
                result.ActionItems.Add("Perform tire rotation and balance");
                result.ActionItems.Add("Inspect battery and coolant level");
            }
            else if (mileageSinceLastService >= 4000)
            {
                result.AlertType = "Inspection Due";
                result.Severity = "Medium";
                result.RequiresImmediateAction = false;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} is approaching regular preventive inspection threshold ({mileageSinceLastService:N0} km since last check).";
                result.ActionItems.Add("Inspect tire tread and pressure");
                result.ActionItems.Add("Check brake fluid and windshield wiper fluid");
            }
            else
            {
                result.AlertType = "Normal";
                result.Severity = "Low";
                result.RequiresImmediateAction = false;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} is operating within healthy operational parameters.";
                result.ActionItems.Add("Routine weekly driver visual inspection");
            }

            return result;
        }

        public async Task<MaintenanceAlert?> CheckAndGenerateAlertAsync(int vehicleId)
        {
            var vehicle = await _context.Vehicles.FindAsync(vehicleId);
            if (vehicle == null) return null;

            var evaluation = EvaluateVehicle(vehicle);

            // If it requires maintenance and has no open alert
            if (evaluation.AlertType != "Normal")
            {
                var existingAlert = await _context.MaintenanceAlerts
                    .FirstOrDefaultAsync(a => a.VehicleID == vehicleId && a.AlertStatus != "Resolved" && a.AlertType == evaluation.AlertType);

                if (existingAlert == null)
                {
                    var alert = new MaintenanceAlert
                    {
                        VehicleID = vehicle.VehicleID,
                        AlertDate = DateTime.UtcNow,
                        AlertType = evaluation.AlertType,
                        Recommendation = evaluation.Recommendation,
                        AlertStatus = "New",
                        CurrentMileageAtAlert = vehicle.CurrentMileage
                    };

                    _context.MaintenanceAlerts.Add(alert);

                    // Update vehicle condition status
                    if (evaluation.AlertType == "Critical Maintenance")
                    {
                        vehicle.ConditionStatus = "Critical";
                        vehicle.AvailabilityStatus = "Under Service";
                    }
                    else if (evaluation.AlertType == "Service Due" && vehicle.ConditionStatus != "Critical")
                    {
                        vehicle.ConditionStatus = "Warning";
                    }

                    await _context.SaveChangesAsync();

                    // Dispatch notification to admins
                    var admins = await _context.Admins.ToListAsync();
                    foreach (var admin in admins)
                    {
                        await _notificationService.CreateNotificationAsync(
                            "Admin",
                            admin.UserID,
                            $"Vehicle Alert: {vehicle.VehicleNumber}",
                            $"{evaluation.AlertType}: {evaluation.Recommendation}",
                            vehicle.VehicleID
                        );
                    }

                    return alert;
                }
            }

            return null;
        }

        public async Task<List<MaintenanceAlert>> EvaluateAllVehiclesAsync()
        {
            var vehicles = await _context.Vehicles.ToListAsync();
            var generated = new List<MaintenanceAlert>();

            foreach (var vehicle in vehicles)
            {
                var alert = await CheckAndGenerateAlertAsync(vehicle.VehicleID);
                if (alert != null)
                {
                    generated.Add(alert);
                }
            }

            return generated;
        }
    }
}
