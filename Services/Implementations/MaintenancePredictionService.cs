using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;

        public MaintenancePredictionService(
            FleetifyDbContext context,
            INotificationService notificationService,
            IEmailService emailService,
            IConfiguration config)
        {
            _context = context;
            _notificationService = notificationService;
            _emailService = emailService;
            _config = config;
        }

        public MaintenancePredictionResult EvaluateVehicle(Vehicle vehicle)
        {
            double mileageSinceLastService = vehicle.CompletedRouteMileage - vehicle.LastServiceMileage;
            if (mileageSinceLastService < 0) mileageSinceLastService = vehicle.CompletedRouteMileage;

            var result = new MaintenancePredictionResult
            {
                VehicleId = vehicle.VehicleID,
                InitialMileage = vehicle.InitialMileage,
                CompletedRouteMileage = vehicle.CompletedRouteMileage,
                MileageSinceLastService = mileageSinceLastService
            };

            // Milestone Evaluation based strictly on cumulative completed route delivery mileage
            if (vehicle.CompletedRouteMileage >= 5000 || vehicle.ConditionStatus.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            {
                result.AlertType = "Full Vehicle Service Due";
                result.Severity = "Critical";
                result.RequiresImmediateAction = true;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} ({vehicle.Model}) has accumulated {vehicle.CompletedRouteMileage:N0} km from completed delivery routes. Immediate full vehicle service and comprehensive mechanical inspection required.";
                result.ActionItems.Add("Take vehicle off active assignments immediately ('Under Service')");
                result.ActionItems.Add("Schedule complete engine diagnostic and fluid flush");
                result.ActionItems.Add("Inspect brake pads, rotors, and transmission fluids");
                result.ActionItems.Add("Perform wheel alignment and suspension inspection");
            }
            else if (vehicle.CompletedRouteMileage >= 3000 || vehicle.ConditionStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                result.AlertType = "Upcoming Oil Change";
                result.Severity = "Warning";
                result.RequiresImmediateAction = true;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} ({vehicle.Model}) has accumulated {vehicle.CompletedRouteMileage:N0} km from completed delivery routes. An upcoming engine oil and filter change is required.";
                result.ActionItems.Add("Schedule engine oil and oil filter replacement");
                result.ActionItems.Add("Inspect engine oil viscosity and dipstick levels");
                result.ActionItems.Add("Inspect coolant and brake fluid levels");
            }
            else if (vehicle.CompletedRouteMileage >= 1500)
            {
                result.AlertType = "Inspection Due";
                result.Severity = "Medium";
                result.RequiresImmediateAction = false;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} has reached {vehicle.CompletedRouteMileage:N0} km of completed route deliveries (approaching preventive inspection).";
                result.ActionItems.Add("Inspect tire tread and pressure");
                result.ActionItems.Add("Check windshield wiper fluid and lighting");
            }
            else
            {
                result.AlertType = "Normal";
                result.Severity = "Low";
                result.RequiresImmediateAction = false;
                result.Recommendation = $"Vehicle {vehicle.VehicleNumber} is operating within healthy operational parameters ({vehicle.CompletedRouteMileage:N0} km route distance completed).";
                result.ActionItems.Add("Routine weekly driver visual inspection");
            }

            return result;
        }

        public async Task<MaintenanceAlert?> ProcessCompletedDeliveryMileageAsync(int vehicleId, double distanceAdded = 0)
        {
            var vehicle = await _context.Vehicles.FindAsync(vehicleId);
            if (vehicle == null) return null;

            // Increment route mileage if distance specified
            if (distanceAdded > 0)
            {
                vehicle.CompletedRouteMileage += distanceAdded;
            }

            // Also synchronize with cumulative distance from all completed delivery requests for this vehicle
            var completedDistance = await _context.Assignments
                .Where(a => a.VehicleID == vehicleId &&
                            a.DeliveryRequest != null &&
                            (a.AssignmentStatus == "Completed" || a.DeliveryRequest.RequestedStatus == "Delivered"))
                .SumAsync(a => (double?)a.DeliveryRequest!.DistanceKm) ?? 0.0;

            if (completedDistance > vehicle.CompletedRouteMileage)
            {
                vehicle.CompletedRouteMileage = completedDistance;
            }

            // Total physical odometer includes initial registration mileage + completed route mileage
            vehicle.CurrentMileage = vehicle.InitialMileage + vehicle.CompletedRouteMileage;

            MaintenanceAlert? generatedAlert = null;

            // 1. Check 5,000 km Milestone: Full Vehicle Service (based strictly on completed route mileage)
            if (vehicle.CompletedRouteMileage >= 5000)
            {
                var existingFullServiceAlert = await _context.MaintenanceAlerts
                    .FirstOrDefaultAsync(a => a.VehicleID == vehicleId &&
                                              a.AlertStatus != "Resolved" &&
                                              (a.AlertType == "Full Vehicle Service Due" || a.AlertType == "Critical Maintenance"));

                if (existingFullServiceAlert == null)
                {
                    generatedAlert = new MaintenanceAlert
                    {
                        VehicleID = vehicle.VehicleID,
                        AlertDate = DateTime.UtcNow,
                        AlertType = "Full Vehicle Service Due",
                        Recommendation = $"Vehicle {vehicle.VehicleNumber} ({vehicle.Model}) has reached cumulative route delivery distance of {vehicle.CompletedRouteMileage:N0} km. Full vehicle service, engine diagnostics, brake inspection, and tire maintenance are required.",
                        AlertStatus = "New",
                        CurrentMileageAtAlert = vehicle.CompletedRouteMileage
                    };

                    _context.MaintenanceAlerts.Add(generatedAlert);
                    vehicle.ConditionStatus = "Critical";
                    vehicle.AvailabilityStatus = "Under Service";
                    await _context.SaveChangesAsync();

                    // Dispatch In-App Notifications
                    await DispatchInAppAdminNotificationsAsync(
                        $"URGENT: Full Vehicle Service Due ({vehicle.VehicleNumber})",
                        $"Vehicle {vehicle.VehicleNumber} reached {vehicle.CompletedRouteMileage:N0} km of completed deliveries. Immediate full overhaul required.",
                        vehicle.VehicleID
                    );

                    // Automatically Send Email to Admin
                    string emailSubject = $"[Fleetify Urgent Alert] 5,000 KM Milestone: Full Vehicle Service Required for {vehicle.VehicleNumber}";
                    string emailBody = $@"URGENT FLEET OPERATIONS ALERT: Full Vehicle Service Required

VEHICLE TELEMETRY:
• Vehicle Number: {vehicle.VehicleNumber}
• Model & Type: {vehicle.Model} ({vehicle.VehicleType})
• Initial Registration Odometer: {vehicle.InitialMileage:N0} km (Excluded from Maintenance)
• Cumulative Route Delivery Mileage: {vehicle.CompletedRouteMileage:N0} km
• Total Physical Odometer: {vehicle.CurrentMileage:N0} km
• Milestone Triggered: 5,000 km Cumulative Route Deliveries Threshold

MAINTENANCE SPECIFICATION:
Vehicle {vehicle.VehicleNumber} has reached the 5,000 km cumulative mileage threshold across completed delivery routes. To ensure roadworthiness and safety compliance:
1. The vehicle has been automatically transitioned to 'Under Service'.
2. Schedule a comprehensive full vehicle service: engine diagnostic tune-up, brake pad & rotor inspection, transmission fluid flush, and tire balance/alignment.
3. Suspend active route dispatches until maintenance has been completed and verified.

Review vehicle status and clear maintenance records in the Fleetify Admin Portal:
http://localhost:5000/Admin/Maintenance";

                    await SendAdminEmailsAsync(emailSubject, emailBody);
                    return generatedAlert;
                }
            }

            // 2. Check 3,000 km Milestone: Upcoming Oil Change (based strictly on completed route mileage)
            if (vehicle.CompletedRouteMileage >= 3000)
            {
                var existingOilAlert = await _context.MaintenanceAlerts
                    .FirstOrDefaultAsync(a => a.VehicleID == vehicleId &&
                                              a.AlertStatus != "Resolved" &&
                                              (a.AlertType == "Upcoming Oil Change" || a.AlertType == "Oil Change Due" || a.AlertType == "Service Due"));

                if (existingOilAlert == null)
                {
                    generatedAlert = new MaintenanceAlert
                    {
                        VehicleID = vehicle.VehicleID,
                        AlertDate = DateTime.UtcNow,
                        AlertType = "Upcoming Oil Change",
                        Recommendation = $"Vehicle {vehicle.VehicleNumber} ({vehicle.Model}) has reached cumulative route delivery distance of {vehicle.CompletedRouteMileage:N0} km. An upcoming engine oil and filter change is required.",
                        AlertStatus = "New",
                        CurrentMileageAtAlert = vehicle.CompletedRouteMileage
                    };

                    _context.MaintenanceAlerts.Add(generatedAlert);
                    if (vehicle.ConditionStatus == "Good")
                    {
                        vehicle.ConditionStatus = "Warning";
                    }
                    await _context.SaveChangesAsync();

                    // Dispatch In-App Notifications
                    await DispatchInAppAdminNotificationsAsync(
                        $"Vehicle Alert: Upcoming Oil Change ({vehicle.VehicleNumber})",
                        $"Vehicle {vehicle.VehicleNumber} reached {vehicle.CompletedRouteMileage:N0} km of completed deliveries. Upcoming engine oil and filter change is due.",
                        vehicle.VehicleID
                    );

                    // Automatically Send Email to Admin
                    string emailSubject = $"[Fleetify Alert] 3,000 KM Milestone: Upcoming Oil Change for Vehicle {vehicle.VehicleNumber}";
                    string emailBody = $@"FLEET OPERATIONS ALERT: Upcoming Oil Change Required

VEHICLE TELEMETRY:
• Vehicle Number: {vehicle.VehicleNumber}
• Model & Type: {vehicle.Model} ({vehicle.VehicleType})
• Initial Registration Odometer: {vehicle.InitialMileage:N0} km (Excluded from Maintenance)
• Cumulative Route Delivery Mileage: {vehicle.CompletedRouteMileage:N0} km
• Total Physical Odometer: {vehicle.CurrentMileage:N0} km
• Milestone Triggered: 3,000 km Cumulative Route Deliveries Threshold

MAINTENANCE SPECIFICATION:
Vehicle {vehicle.VehicleNumber} has accumulated {vehicle.CompletedRouteMileage:N0} km across completed route deliveries. Under fleet maintenance protocol:
1. An upcoming engine oil and oil filter change is required.
2. Inspect engine oil level, viscosity, and check for minor leaks or fluid top-ups.
3. Schedule servicing before commencing prolonged inter-city transit.

Review vehicle maintenance records in the Fleetify Admin Portal:
http://localhost:5000/Admin/Maintenance";

                    await SendAdminEmailsAsync(emailSubject, emailBody);
                    return generatedAlert;
                }
            }

            await _context.SaveChangesAsync();
            return null;
        }

        public async Task<MaintenanceAlert?> CheckAndGenerateAlertAsync(int vehicleId)
        {
            // First check the 3,000 km and 5,000 km cumulative mileage milestones
            var milestoneAlert = await ProcessCompletedDeliveryMileageAsync(vehicleId, 0);
            if (milestoneAlert != null) return milestoneAlert;

            var vehicle = await _context.Vehicles.FindAsync(vehicleId);
            if (vehicle == null) return null;

            var evaluation = EvaluateVehicle(vehicle);

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
                        CurrentMileageAtAlert = vehicle.CompletedRouteMileage
                    };

                    _context.MaintenanceAlerts.Add(alert);

                    if (evaluation.Severity == "Critical")
                    {
                        vehicle.ConditionStatus = "Critical";
                        vehicle.AvailabilityStatus = "Under Service";
                    }
                    else if (evaluation.Severity == "Warning" || evaluation.Severity == "High")
                    {
                        if (vehicle.ConditionStatus != "Critical") vehicle.ConditionStatus = "Warning";
                    }

                    await _context.SaveChangesAsync();

                    await DispatchInAppAdminNotificationsAsync(
                        $"Vehicle Alert: {vehicle.VehicleNumber}",
                        $"{evaluation.AlertType}: {evaluation.Recommendation}",
                        vehicle.VehicleID
                    );

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

        private async Task DispatchInAppAdminNotificationsAsync(string title, string message, int vehicleId)
        {
            var admins = await _context.Admins.ToListAsync();
            foreach (var admin in admins)
            {
                await _notificationService.CreateNotificationAsync(
                    "Admin",
                    admin.UserID,
                    title,
                    message,
                    vehicleId
                );
            }
        }

        private async Task SendAdminEmailsAsync(string subject, string body)
        {
            var adminEmails = await _context.Admins
                .Where(a => !string.IsNullOrWhiteSpace(a.Email))
                .Select(a => a.Email.Trim())
                .Distinct()
                .ToListAsync();

            if (!adminEmails.Any())
            {
                adminEmails.Add("admin@fleetify.com");
            }

            var senderEmail = _config["EmailSettings:SenderEmail"] ?? "fypfleetify@gmail.com";
            if (!string.IsNullOrWhiteSpace(senderEmail) && !adminEmails.Contains(senderEmail, StringComparer.OrdinalIgnoreCase))
            {
                adminEmails.Add(senderEmail);
            }

            foreach (var email in adminEmails)
            {
                try
                {
                    await _emailService.SendEmailAsync(email, subject, body);
                }
                catch
                {
                    // Email logging is handled by EmailService
                }
            }
        }
    }
}
