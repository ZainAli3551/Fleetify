using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Fleetify.Data;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly FleetifyDbContext _context;
        private readonly IMaintenancePredictionService _maintenanceService;
        private readonly INotificationService _notificationService;
        private readonly IAuthService _authService;
        private readonly ISupportService _supportService;

        public AdminController(
            FleetifyDbContext context,
            IMaintenancePredictionService maintenanceService,
            INotificationService notificationService,
            IAuthService authService,
            ISupportService supportService)
        {
            _context = context;
            _maintenanceService = maintenanceService;
            _notificationService = notificationService;
            _authService = authService;
            _supportService = supportService;
        }

        // GET: /Admin (Figure 21: Admin Dashboard)
        public async Task<IActionResult> Index()
        {
            var totalUsers = await _context.Users.CountAsync();
            var activeDrivers = await _context.Drivers.CountAsync(d => d.AvailabilityStatus == "Available" || d.AvailabilityStatus == "Busy");
            var totalDeliveries = await _context.DeliveryRequests.CountAsync();
            var totalRevenue = await _context.DeliveryRequests.SumAsync(d => d.EstimatedCost);

            var today = DateTime.UtcNow.Date;
            var completedToday = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "Delivered" && d.RequestDate >= today);
            var inTransit = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "In-Transit");
            var pending = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "Pending");

            var recentDeliveries = await _context.DeliveryRequests
                .Include(d => d.User)
                .Include(d => d.Assignment)
                    .ThenInclude(a => a!.Driver)
                .OrderByDescending(d => d.RequestDate)
                .Take(5)
                .ToListAsync();

            var alerts = await _context.MaintenanceAlerts
                .Include(a => a.Vehicle)
                .Where(a => a.AlertStatus != "Resolved")
                .OrderByDescending(a => a.AlertDate)
                .Take(5)
                .ToListAsync();

            var viewModel = new AdminDashboardViewModel
            {
                TotalUsers = totalUsers,
                ActiveDrivers = activeDrivers,
                TotalDeliveries = totalDeliveries,
                Revenue = Math.Round(totalRevenue, 2),
                CompletedToday = completedToday,
                InTransitCount = inTransit,
                PendingCount = pending,
                RecentDeliveries = recentDeliveries,
                ActiveMaintenanceAlerts = alerts
            };

            return View(viewModel);
        }

        // GET: /Admin/Drivers
        public async Task<IActionResult> Drivers()
        {
            var drivers = await _context.Drivers
                .Include(d => d.Assignments)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
            return View(drivers);
        }

        // POST: /Admin/CreateDriver
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDriver(RegisterViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
            {
                TempData["ErrorMessage"] = "Please provide all required fields for driver registration.";
                return RedirectToAction(nameof(Drivers));
            }

            if (await _authService.UserExistsAsync(model.Email))
            {
                TempData["ErrorMessage"] = "A user with this email already exists.";
                return RedirectToAction(nameof(Drivers));
            }

            model.Role = "Driver";
            await _authService.RegisterDriverAsync(model);
            TempData["SuccessMessage"] = $"Driver '{model.FullName}' created successfully!";
            return RedirectToAction(nameof(Drivers));
        }

        // POST: /Admin/ToggleDriverStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDriverStatus(int id, string status)
        {
            var driver = await _context.Drivers.FindAsync(id);
            if (driver != null)
            {
                driver.AvailabilityStatus = status;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Driver status updated to '{status}'.";
            }
            return RedirectToAction(nameof(Drivers));
        }

        // GET: /Admin/Vehicles
        public async Task<IActionResult> Vehicles()
        {
            var vehicles = await _context.Vehicles
                .Include(v => v.MaintenanceAlerts)
                .OrderBy(v => v.VehicleID)
                .ToListAsync();
            return View(vehicles);
        }

        // POST: /Admin/CreateVehicle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateVehicle(Vehicle vehicle)
        {
            if (string.IsNullOrWhiteSpace(vehicle.VehicleNumber) || string.IsNullOrWhiteSpace(vehicle.Model))
            {
                TempData["ErrorMessage"] = "Please fill out all required vehicle details.";
                return RedirectToAction(nameof(Vehicles));
            }

            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();

            // Run initial predictive maintenance check
            await _maintenanceService.CheckAndGenerateAlertAsync(vehicle.VehicleID);

            TempData["SuccessMessage"] = $"Vehicle '{vehicle.VehicleNumber}' added to fleet successfully!";
            return RedirectToAction(nameof(Vehicles));
        }

        // GET: /Admin/Assignments
        public async Task<IActionResult> Assignments()
        {
            var pendingRequests = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                .Where(r => r.Assignment == null || r.RequestedStatus == "Pending")
                .ToListAsync();

            var activeDrivers = await _context.Drivers
                .Where(d => d.AvailabilityStatus != "Offline")
                .ToListAsync();

            var availableVehicles = await _context.Vehicles
                .Where(v => v.AvailabilityStatus != "Under Service")
                .ToListAsync();

            ViewBag.Drivers = activeDrivers;
            ViewBag.Vehicles = availableVehicles;

            var existingAssignments = await _context.Assignments
                .Include(a => a.DeliveryRequest)
                    .ThenInclude(r => r!.User)
                .Include(a => a.Driver)
                .Include(a => a.Vehicle)
                .OrderByDescending(a => a.AssignedDate)
                .ToListAsync();

            ViewBag.Assignments = existingAssignments;

            return View(pendingRequests);
        }

        // POST: /Admin/AssignDelivery
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignDelivery(int requestId, int driverId, int vehicleId)
        {
            var deliveryRequest = await _context.DeliveryRequests.FindAsync(requestId);
            var driver = await _context.Drivers.FindAsync(driverId);
            var vehicle = await _context.Vehicles.FindAsync(vehicleId);

            if (deliveryRequest == null || driver == null || vehicle == null)
            {
                TempData["ErrorMessage"] = "Invalid delivery request, driver, or vehicle.";
                return RedirectToAction(nameof(Assignments));
            }

            var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(adminIdClaim, out int adminId);

            var assignment = await _context.Assignments.FirstOrDefaultAsync(a => a.RequestID == requestId);
            if (assignment != null)
            {
                assignment.DriverID = driverId;
                assignment.VehicleID = vehicleId;
                assignment.AdminID = adminId > 0 ? adminId : null;
                assignment.AssignedDate = DateTime.UtcNow;
                assignment.AssignmentStatus = "Pending";
            }
            else
            {
                assignment = new Assignment
                {
                    RequestID = requestId,
                    DriverID = driverId,
                    VehicleID = vehicleId,
                    AdminID = adminId > 0 ? adminId : null,
                    AssignedDate = DateTime.UtcNow,
                    AssignmentStatus = "Pending"
                };
                _context.Assignments.Add(assignment);
            }

            deliveryRequest.RequestedStatus = "Assigned";
            driver.AvailabilityStatus = "Busy";
            vehicle.AvailabilityStatus = "Assigned";

            await _context.SaveChangesAsync();

            // Send notification to Driver and Customer
            await _notificationService.CreateNotificationAsync(
                "Driver",
                driverId,
                "New Assignment",
                $"You have been assigned delivery {deliveryRequest.TrackingNumber}.",
                assignment.AssignmentID
            );

            await _notificationService.CreateNotificationAsync(
                "Customer",
                deliveryRequest.UserID,
                "Delivery Assigned",
                $"Your shipment {deliveryRequest.TrackingNumber} has been assigned to driver {driver.FullName}.",
                deliveryRequest.RequestID
            );

            TempData["SuccessMessage"] = $"Delivery {deliveryRequest.TrackingNumber} successfully assigned to {driver.FullName}!";
            return RedirectToAction(nameof(Assignments));
        }

        // GET: /Admin/Maintenance
        public async Task<IActionResult> Maintenance()
        {
            var alerts = await _context.MaintenanceAlerts
                .Include(a => a.Vehicle)
                .OrderByDescending(a => a.AlertDate)
                .ToListAsync();

            var vehicles = await _context.Vehicles.ToListAsync();
            ViewBag.Vehicles = vehicles;

            return View(alerts);
        }

        // POST: /Admin/RunMaintenanceDiagnostic
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunMaintenanceDiagnostic()
        {
            var newAlerts = await _maintenanceService.EvaluateAllVehiclesAsync();
            TempData["SuccessMessage"] = $"AI diagnostic completed! Evaluated fleet vehicles; {newAlerts.Count} new maintenance alert(s) identified.";
            return RedirectToAction(nameof(Maintenance));
        }

        // POST: /Admin/ResolveAlert
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveAlert(int alertId)
        {
            var alert = await _context.MaintenanceAlerts
                .Include(a => a.Vehicle)
                .FirstOrDefaultAsync(a => a.AlertID == alertId);

            if (alert != null)
            {
                alert.AlertStatus = "Resolved";
                if (alert.Vehicle != null)
                {
                    alert.Vehicle.ConditionStatus = "Good";
                    alert.Vehicle.AvailabilityStatus = "Available";
                    alert.Vehicle.LastServiceMileage = alert.Vehicle.CurrentMileage;
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Alert #{alert.AlertID} resolved. Vehicle serviced and marked operational.";
            }
            return RedirectToAction(nameof(Maintenance));
        }

        // GET: /Admin/Support (Hybrid Support System Dashboard)
        public async Task<IActionResult> Support(string? statusFilter = "All", int? conversationId = null)
        {
            var allConversations = await _context.SupportConversations.ToListAsync();
            var totalTickets = allConversations.Count;
            var botHandled = allConversations.Count(c => c.Status == "BotHandled");
            var needsAdmin = allConversations.Count(c => c.Status == "NeedsAdmin");
            var resolved = allConversations.Count(c => c.Status == "Resolved" || c.Status == "Closed");

            var filteredList = await _supportService.GetConversationsAsync(statusFilter);

            SupportConversation? activeConv = null;
            if (conversationId.HasValue)
            {
                activeConv = await _supportService.GetConversationDetailsAsync(conversationId.Value);
            }
            else if (filteredList.Any())
            {
                activeConv = await _supportService.GetConversationDetailsAsync(filteredList.First().ConversationID);
            }

            var vm = new AdminSupportDashboardViewModel
            {
                TotalTickets = totalTickets,
                BotHandledCount = botHandled,
                NeedsAdminCount = needsAdmin,
                ResolvedCount = resolved,
                CurrentFilter = statusFilter ?? "All",
                Conversations = filteredList,
                ActiveConversation = activeConv
            };

            return View(vm);
        }

        // POST: /Admin/SupportReply (Admin Intervention)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SupportReply(int conversationId, string replyMessage, bool resolve = false)
        {
            if (string.IsNullOrWhiteSpace(replyMessage))
            {
                TempData["ErrorMessage"] = "Reply message cannot be empty.";
                return RedirectToAction(nameof(Support), new { conversationId });
            }

            var adminName = User.Identity?.Name ?? "Administrator";
            var adminIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(adminIdStr, out int adminId);

            await _supportService.AddAdminReplyAsync(conversationId, adminId, adminName, replyMessage, resolve);
            TempData["SuccessMessage"] = resolve 
                ? "Admin reply dispatched and ticket marked resolved!" 
                : "Admin reply dispatched directly to user.";

            return RedirectToAction(nameof(Support), new { conversationId });
        }

        // POST: /Admin/ResolveTicket
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveTicket(int conversationId)
        {
            await _supportService.ResolveTicketAsync(conversationId);
            TempData["SuccessMessage"] = "Ticket successfully resolved.";
            return RedirectToAction(nameof(Support), new { conversationId });
        }
    }
}
