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
using Fleetify.Services;
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
        private readonly ICostEstimationService _costService;
        private readonly IEmailService _emailService;

        public AdminController(
            FleetifyDbContext context,
            IMaintenancePredictionService maintenanceService,
            INotificationService notificationService,
            IAuthService authService,
            ISupportService supportService,
            ICostEstimationService costService,
            IEmailService emailService)
        {
            _context = context;
            _maintenanceService = maintenanceService;
            _notificationService = notificationService;
            _authService = authService;
            _supportService = supportService;
            _costService = costService;
            _emailService = emailService;
        }

        // GET: /Admin (Admin Dashboard)
        public async Task<IActionResult> Index()
        {
            var totalUsers = await _context.Users.CountAsync();
            var activeDrivers = await _context.Drivers.CountAsync(d => d.AvailabilityStatus == "Available" || d.AvailabilityStatus == "Busy");
            var totalDeliveries = await _context.DeliveryRequests.CountAsync();
            var totalRevenue = await _context.DeliveryRequests.SumAsync(d => d.EstimatedCost);

            var completedTotal = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "Delivered");
            var inTransit = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "In-Transit");
            var pending = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "Pending");

            var recentDeliveries = await _context.DeliveryRequests
                .Include(d => d.User)
                .Include(d => d.Assignment)
                    .ThenInclude(a => a!.Driver)
                .Include(d => d.Assignment)
                    .ThenInclude(a => a!.Vehicle)
                .OrderByDescending(d => d.RequestDate)
                .Take(10)
                .ToListAsync();

            var alerts = await _context.MaintenanceAlerts
                .Include(a => a.Vehicle)
                .Where(a => a.AlertStatus != "Resolved")
                .OrderByDescending(a => a.AlertDate)
                .Take(5)
                .ToListAsync();

            var drivers = await _context.Drivers.ToListAsync();
            var vehicles = await _context.Vehicles.ToListAsync();
            var users = await _context.Users.ToListAsync();

            var viewModel = new AdminDashboardViewModel
            {
                TotalUsers = totalUsers,
                ActiveDrivers = activeDrivers,
                TotalDeliveries = totalDeliveries,
                Revenue = Math.Round(totalRevenue, 2),
                CompletedToday = completedTotal,
                InTransitCount = inTransit,
                PendingCount = pending,
                RecentDeliveries = recentDeliveries,
                ActiveMaintenanceAlerts = alerts,
                Drivers = drivers,
                Vehicles = vehicles,
                UsersList = users,
                RevenueTrend = new List<double> { 15000, 20000, 26000, 23000, 35000, 31000, 48000 },
                WeeklyDeliveries = new List<int> { 7, 9, 11, 9, 16, 11, 7 }
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

            ViewBag.CompanyDrivers = drivers.Where(d => d.DriverType != "Backup").ToList();
            ViewBag.BackupDrivers = drivers.Where(d => d.DriverType == "Backup").ToList();

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
            if (string.IsNullOrWhiteSpace(model.DriverType))
            {
                model.DriverType = "Company";
            }

            await _authService.RegisterDriverAsync(model);
            string typeLabel = model.DriverType == "Backup" ? "On-Call Backup Driver" : "Company Fleet Driver";
            TempData["SuccessMessage"] = $"{typeLabel} '{model.FullName}' added successfully!";
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

            // Initial registration mileage is recorded and excluded from maintenance calculations
            vehicle.InitialMileage = vehicle.CurrentMileage;
            vehicle.CompletedRouteMileage = 0.0;

            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();

            // Run initial predictive maintenance check (evaluated on CompletedRouteMileage = 0.0)
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

            var companyDrivers = activeDrivers.Where(d => d.DriverType != "Backup").ToList();
            var backupDrivers = await _context.Drivers.Where(d => d.DriverType == "Backup").ToListAsync();

            ViewBag.Drivers = activeDrivers;
            ViewBag.CompanyDrivers = companyDrivers;
            ViewBag.BackupDrivers = backupDrivers;
            ViewBag.Vehicles = availableVehicles;

            var existingAssignments = await _context.Assignments
                .Include(a => a.DeliveryRequest)
                    .ThenInclude(r => r!.User)
                .Include(a => a.Driver)
                .Include(a => a.Vehicle)
                .OrderByDescending(a => a.AssignedDate)
                .ToListAsync();

            var verificationRequests = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Driver)
                .Where(r => r.WeightStatus == "VerifiedMatched" || r.WeightStatus == "DiscrepancyReported")
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            // All active shipments in transit or reached warehouse
            var activeShipments = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Driver)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Vehicle)
                .Where(r => r.RequestedStatus != "Delivered" && r.RequestedStatus != "Cancelled" && r.RequestedStatus != "Pending")
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            ViewBag.Assignments = existingAssignments;
            ViewBag.VerificationRequests = verificationRequests;
            ViewBag.ActiveShipments = activeShipments;

            return View(pendingRequests);
        }

        // GET: /Admin/Deliveries
        public async Task<IActionResult> Deliveries(string statusFilter = "All", string search = "")
        {
            var query = _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Driver)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Vehicle)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(r => 
                    r.TrackingNumber.Contains(search) || 
                    (r.User != null && r.User.FullName.Contains(search)) ||
                    r.PickupLocation.Contains(search) ||
                    r.DropoffLocation.Contains(search) ||
                    (r.PackageName != null && r.PackageName.Contains(search))
                );
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
            {
                query = query.Where(r => r.RequestedStatus == statusFilter);
            }

            var allDeliveries = await query.OrderByDescending(r => r.RequestDate).ToListAsync();

            var allCount = await _context.DeliveryRequests.CountAsync();
            var pendingCount = await _context.DeliveryRequests.CountAsync(r => r.RequestedStatus == "Pending");
            var assignedCount = await _context.DeliveryRequests.CountAsync(r => r.RequestedStatus == "Assigned");
            var inTransitCount = await _context.DeliveryRequests.CountAsync(r => r.RequestedStatus == "In-Transit");
            var deliveredCount = await _context.DeliveryRequests.CountAsync(r => r.RequestedStatus == "Delivered");
            var cancelledCount = await _context.DeliveryRequests.CountAsync(r => r.RequestedStatus == "Cancelled");

            ViewBag.CurrentFilter = statusFilter;
            ViewBag.SearchTerm = search;
            ViewBag.TotalCount = allCount;
            ViewBag.PendingCount = pendingCount;
            ViewBag.AssignedCount = assignedCount;
            ViewBag.InTransitCount = inTransitCount;
            ViewBag.DeliveredCount = deliveredCount;
            ViewBag.CancelledCount = cancelledCount;

            return View(allDeliveries);
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

            // Send in-portal notification to Driver
            await _notificationService.CreateNotificationAsync(
                "Driver",
                driverId,
                "New Assignment",
                $"You have been assigned delivery {deliveryRequest.TrackingNumber}.",
                assignment.AssignmentID
            );

            // Send notification to Customer via Gmail (not on customer portal)
            var customer = await _context.Users.FindAsync(deliveryRequest.UserID);
            if (customer != null && !string.IsNullOrWhiteSpace(customer.Email))
            {
                string subject = $"Fleetify: Shipment {deliveryRequest.TrackingNumber} Assigned to Driver";
                string body = $@"Dear {customer.FullName},

Aap ki shipment {deliveryRequest.TrackingNumber} driver {driver.FullName} ko assign kar di gayi hai.

Tracking Number: {deliveryRequest.TrackingNumber}
Assigned Driver: {driver.FullName}
Fleet Vehicle: {vehicle.VehicleNumber} ({vehicle.VehicleType})
Route: {deliveryRequest.PickupLocation} -> {deliveryRequest.DropoffLocation}
Current Status: Assigned

Yeh status update aap ke registered Gmail address par bheja gaya hai.";

                await _emailService.SendEmailAsync(customer.Email, subject, body);
            }

            TempData["SuccessMessage"] = $"Delivery {deliveryRequest.TrackingNumber} successfully assigned to {driver.FullName}! Gmail notification sent to customer ({customer?.Email}).";
            return RedirectToAction(nameof(Assignments));
        }

        // POST: /Admin/FinalizeDeliveryCost
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizeDeliveryCost(int requestId, double finalWeight)
        {
            var deliveryRequest = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Driver)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (deliveryRequest == null)
            {
                TempData["ErrorMessage"] = "Delivery request not found.";
                return RedirectToAction(nameof(Assignments));
            }

            if (finalWeight <= 0)
            {
                TempData["ErrorMessage"] = "Please provide a valid parcel weight in kg.";
                return RedirectToAction(nameof(Assignments));
            }

            double oldWeight = deliveryRequest.ParcelWeight;
            double oldCost = deliveryRequest.EstimatedCost;
            bool weightChanged = Math.Abs(finalWeight - oldWeight) >= 0.01;

            double newCost = oldCost;
            if (weightChanged)
            {
                var costReq = new Models.ViewModels.CostEstimateRequest
                {
                    PickupLocation = deliveryRequest.PickupLocation,
                    DropoffLocation = deliveryRequest.DropoffLocation,
                    DistanceKm = deliveryRequest.DistanceKm,
                    ParcelWeight = finalWeight,
                    VehicleType = deliveryRequest.VehicleType,
                    RouteType = deliveryRequest.RouteType
                };
                var est = _costService.CalculateCost(costReq);
                newCost = est.EstimatedTotal;
                deliveryRequest.ParcelWeight = finalWeight;
                deliveryRequest.EstimatedCost = newCost;

                await _costService.SaveCostEstimateAsync(deliveryRequest.RequestID, costReq, est);
            }

            deliveryRequest.VerifiedWeight = finalWeight;
            deliveryRequest.WeightStatus = "Finalized";
            if (deliveryRequest.RequestedStatus == "Pending" || deliveryRequest.RequestedStatus == "Weight Verified" || deliveryRequest.RequestedStatus == "Weight Reported")
            {
                deliveryRequest.RequestedStatus = deliveryRequest.Assignment != null ? "In-Transit" : "Approved";
            }

            if (deliveryRequest.Assignment != null)
            {
                _context.StatusUpdates.Add(new StatusUpdate
                {
                    AssignmentID = deliveryRequest.Assignment.AssignmentID,
                    DriverID = deliveryRequest.Assignment.DriverID,
                    UpdateStatus = "Cost Finalized",
                    UpdateTime = DateTime.UtcNow,
                    Remarks = weightChanged
                        ? $"Admin finalized cost at Rs. {Math.Floor(newCost):F0} based on updated weight {finalWeight:F1} kg (Customer declared: {oldWeight:F1} kg)."
                        : $"Admin finalized cost at Rs. {Math.Floor(newCost):F0} (Verified weight: {finalWeight:F1} kg)."
                });
            }

            await _context.SaveChangesAsync();

            // Send notification to Customer via Gmail (not on customer portal)
            var customer = deliveryRequest.User ?? await _context.Users.FindAsync(deliveryRequest.UserID);
            if (customer != null && !string.IsNullOrWhiteSpace(customer.Email))
            {
                string subject = weightChanged 
                    ? $"Fleetify: Shipment {deliveryRequest.TrackingNumber} - Updated Cost & Weight Verified"
                    : $"Fleetify: Shipment {deliveryRequest.TrackingNumber} - Delivery Cost Finalized";
                string body = weightChanged
                    ? $@"Dear {customer.FullName},

Driver ne parcel ka actual weight {finalWeight:F1} kg verify kiya hai (previously declared: {oldWeight:F1} kg). Aap ki delivery cost update karke Rs. {Math.Floor(newCost):F0} finalize kar di gayi hai.

Tracking Number: {deliveryRequest.TrackingNumber}
Verified Weight: {finalWeight:F1} kg
Final Amount: Rs. {Math.Floor(newCost):F0}
Status: {deliveryRequest.RequestedStatus}

Yeh notification aap ke registered Gmail address par bheja gaya hai."
                    : $@"Dear {customer.FullName},

Aap ke parcel ka weight {finalWeight:F1} kg verify ho chuka hai aur delivery cost Rs. {Math.Floor(newCost):F0} finalize kar di gayi hai.

Tracking Number: {deliveryRequest.TrackingNumber}
Verified Weight: {finalWeight:F1} kg
Final Amount: Rs. {Math.Floor(newCost):F0}
Status: {deliveryRequest.RequestedStatus}

Yeh notification aap ke registered Gmail address par bheja gaya hai.";

                await _emailService.SendEmailAsync(customer.Email, subject, body);
            }

            TempData["SuccessMessage"] = $"Delivery {deliveryRequest.TrackingNumber} finalized! Weight: {finalWeight:F1} kg, Final Cost: Rs. {Math.Floor(newCost):F0}. Gmail notification sent to customer ({customer?.Email}).";
            return RedirectToAction(nameof(Assignments));
        }

        // POST: /Admin/ConfirmWarehouseArrival
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmWarehouseArrival(int requestId)
        {
            return await MarkWarehouseArrival(requestId);
        }

        // POST: /Admin/MarkWarehouseArrival
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkWarehouseArrival(int requestId)
        {
            var req = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Driver)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (req == null)
            {
                TempData["ErrorMessage"] = "Delivery request not found.";
                return RedirectToAction(nameof(Assignments));
            }

            var destWh = WarehouseNetworkService.GetWarehouse(req.DropoffLocation);
            req.RequestedStatus = "ArrivedAtWarehouse";
            req.WarehouseArrivalNotified = true;
            if (destWh != null)
            {
                req.DestinationWarehouseName = destWh.HubName;
                req.DestinationWarehouseAddress = destWh.Address;
            }

            if (req.Assignment != null)
            {
                _context.StatusUpdates.Add(new StatusUpdate
                {
                    AssignmentID = req.Assignment.AssignmentID,
                    DriverID = req.Assignment.DriverID,
                    UpdateStatus = "ArrivedAtWarehouse",
                    UpdateTime = DateTime.UtcNow,
                    Remarks = $"Shipment arrived at destination hub ({destWh?.HubName ?? "Local Hub"}). Admin confirmed delivery arrival."
                });
            }

            await _context.SaveChangesAsync();

            // Send notification to Customer via Gmail (not on customer portal)
            string whName = destWh?.HubName ?? "Fleetify Destination Warehouse Hub";
            string whAddr = destWh?.Address ?? "Fleetify Warehouse Hub";
            var customer = req.User ?? await _context.Users.FindAsync(req.UserID);
            if (customer != null && !string.IsNullOrWhiteSpace(customer.Email))
            {
                string subject = $"Fleetify: Shipment {req.TrackingNumber} Arrived at Warehouse & Delivery Confirmed";
                string body = $@"Dear {customer.FullName},

Aap ka saman warehouse me pohanch gaya hai aur Admin ne delivery confirm kar di hai!

Tracking Number: {req.TrackingNumber}
Warehouse Hub: {whName} ({whAddr})
Status: Arrived at Warehouse (Delivery Confirmed)

Aap ka status warehouse me saman pohanchne par update kar diya gaya hai.
{(req.DestinationType == "WarehousePickup" 
    ? "Aap ne Warehouse Self-Pickup chuni thi, lehaza aap hub se apna parcel collect kar sakte hain." 
    : "Doorstep delivery ke mutabiq courier rider aap ke pate par parcel deliver kare ga.")}

Yeh notification aap ke registered Gmail address par bheja gaya hai.";

                await _emailService.SendEmailAsync(customer.Email, subject, body);
            }

            TempData["SuccessMessage"] = $"Shipment {req.TrackingNumber} marked as arrived at {whName} & delivery confirmed! Gmail notification sent to customer ({customer?.Email}).";
            return RedirectToAction(nameof(Assignments));
        }

        // POST: /Admin/UpdateDeliveryMilestone
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDeliveryMilestone(int requestId, string status, string? remarks, string? returnUrl = null)
        {
            var req = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (req == null)
            {
                TempData["ErrorMessage"] = "Delivery request not found.";
                return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) 
                    ? Redirect(returnUrl) 
                    : RedirectToAction(nameof(Assignments));
            }

            req.RequestedStatus = status;

            if (req.Assignment != null)
            {
                _context.StatusUpdates.Add(new StatusUpdate
                {
                    AssignmentID = req.Assignment.AssignmentID,
                    DriverID = req.Assignment.DriverID,
                    UpdateStatus = status,
                    UpdateTime = DateTime.UtcNow,
                    Remarks = string.IsNullOrWhiteSpace(remarks) ? $"Status updated to '{status}' by Operations Admin." : remarks
                });

                if (status == "Delivered")
                {
                    req.Assignment.AssignmentStatus = "Completed";
                    var driver = await _context.Drivers.FindAsync(req.Assignment.DriverID);
                    if (driver != null) driver.AvailabilityStatus = "Available";
                    var vehicle = await _context.Vehicles.FindAsync(req.Assignment.VehicleID);
                    if (vehicle != null) vehicle.AvailabilityStatus = "Available";
                }
            }

            await _context.SaveChangesAsync();

            // Monitor cumulative vehicle mileage and trigger 3,000 / 5,000 km alerts + admin emails
            if (status == "Delivered" && req.Assignment != null && req.Assignment.VehicleID > 0)
            {
                await _maintenanceService.ProcessCompletedDeliveryMileageAsync(req.Assignment.VehicleID, req.DistanceKm);
            }

            // Send notification to Customer via Gmail (not on customer portal)
            var customer = req.User ?? await _context.Users.FindAsync(req.UserID);
            if (customer != null && !string.IsNullOrWhiteSpace(customer.Email))
            {
                string subject = $"Fleetify: Shipment {req.TrackingNumber} Status Update - {status}";
                string body = $@"Dear {customer.FullName},

Aap ki shipment {req.TrackingNumber} ka status Admin ki taraf se update kar diya gaya hai.

New Status: {status}
Remarks: {remarks ?? "Operations milestone update."}

Yeh notification aap ke registered Gmail address par bheja gaya hai.";

                await _emailService.SendEmailAsync(customer.Email, subject, body);
            }

            TempData["SuccessMessage"] = $"Shipment {req.TrackingNumber} status updated to '{status}'. Gmail notification sent to customer ({customer?.Email}).";
            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) 
                ? Redirect(returnUrl) 
                : RedirectToAction(nameof(Assignments));
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
                    alert.Vehicle.LastServiceMileage = alert.Vehicle.CompletedRouteMileage;
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
