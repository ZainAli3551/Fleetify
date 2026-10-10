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
using Fleetify.Services.Implementations;

namespace Fleetify.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CustomerController : Controller
    {
        private readonly FleetifyDbContext _context;
        private readonly ICostEstimationService _costService;
        private readonly INotificationService _notificationService;
        private readonly IMapRoutingService _mapRoutingService;

        public CustomerController(
            FleetifyDbContext context,
            ICostEstimationService costService,
            INotificationService notificationService,
            IMapRoutingService mapRoutingService)
        {
            _context = context;
            _costService = costService;
            _notificationService = notificationService;
            _mapRoutingService = mapRoutingService;
        }

        // GET: /Customer (Figure 23: Customer Portal)
        public async Task<IActionResult> Index()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account", new { role = "Customer" });
            }

            var user = await _context.Users
                .Include(u => u.DeliveryRequests)
                    .ThenInclude(r => r.Assignment)
                        .ThenInclude(a => a!.Driver)
                .FirstOrDefaultAsync(u => u.UserID == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Account", new { role = "Customer" });
            }

            var deliveries = user.DeliveryRequests.OrderByDescending(r => r.RequestDate).ToList();

            var totalDeliveries = deliveries.Count;
            var inTransit = deliveries.Count(d => d.RequestedStatus == "In-Transit" || d.RequestedStatus == "Assigned");
            var delivered = deliveries.Count(d => d.RequestedStatus == "Delivered");
            var totalSpent = deliveries.Sum(d => d.EstimatedCost);
            var notifications = _notificationService != null
                ? await _notificationService.GetUserNotificationsAsync("Customer", userId, 10)
                : new List<Notification>();

            var viewModel = new CustomerDashboardViewModel
            {
                UserId = user.UserID,
                UserName = user.FullName,
                TotalDeliveries = totalDeliveries,
                InTransitCount = inTransit,
                DeliveredCount = delivered,
                TotalSpent = Math.Round(totalSpent, 2),
                MyDeliveries = deliveries,
                Notifications = notifications
            };

            return View(viewModel);
        }

        // POST: /Customer/CreateDelivery
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDelivery([Bind(Prefix = "NewDelivery")] DeliveryRequestInputModel model)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account", new { role = "Customer" });
            }

            // Fallback checking in case prefix was omitted or present in Request.Form
            if (string.IsNullOrWhiteSpace(model.PickupLocation))
            {
                model.PickupLocation = Request.Form["NewDelivery.PickupLocation"].ToString();
                if (string.IsNullOrWhiteSpace(model.PickupLocation))
                {
                    model.PickupLocation = Request.Form["PickupLocation"].ToString();
                }
            }
            if (string.IsNullOrWhiteSpace(model.DropoffLocation))
            {
                model.DropoffLocation = Request.Form["NewDelivery.DropoffLocation"].ToString();
                if (string.IsNullOrWhiteSpace(model.DropoffLocation))
                {
                    model.DropoffLocation = Request.Form["DropoffLocation"].ToString();
                }
            }
            if (double.TryParse(Request.Form["NewDelivery.ParcelWeight"], out var w) || double.TryParse(Request.Form["ParcelWeight"], out w))
            {
                model.ParcelWeight = Math.Max(0.0, w);
            }
            else if (model.ParcelWeight < 0)
            {
                model.ParcelWeight = 0.0;
            }

            if (model.Length <= 0)
            {
                if (double.TryParse(Request.Form["NewDelivery.Length"], out var l) || double.TryParse(Request.Form["Length"], out l))
                {
                    model.Length = l;
                }
                else
                {
                    model.Length = 30.0;
                }
            }

            if (model.Height <= 0)
            {
                if (double.TryParse(Request.Form["NewDelivery.Height"], out var h) || double.TryParse(Request.Form["Height"], out h))
                {
                    model.Height = h;
                }
                else
                {
                    model.Height = 15.0;
                }
            }

            if (model.Width <= 0)
            {
                if (double.TryParse(Request.Form["NewDelivery.Width"], out var wd) || double.TryParse(Request.Form["Width"], out wd))
                {
                    model.Width = wd;
                }
                else
                {
                    model.Width = 20.0;
                }
            }

            if (string.IsNullOrWhiteSpace(model.VehicleType))
            {
                var vForm = Request.Form["NewDelivery.VehicleType"].ToString();
                model.VehicleType = !string.IsNullOrWhiteSpace(vForm) ? vForm : "Truck";
            }

            if (string.IsNullOrWhiteSpace(model.RouteType))
            {
                model.RouteType = Request.Form["NewDelivery.RouteType"].ToString();
                if (string.IsNullOrWhiteSpace(model.RouteType)) model.RouteType = "Normal";
            }
            if (string.IsNullOrWhiteSpace(model.ParcelDescription))
            {
                model.ParcelDescription = Request.Form["NewDelivery.ParcelDescription"].ToString();
                if (string.IsNullOrWhiteSpace(model.ParcelDescription)) model.ParcelDescription = "General Package";
            }

            if (string.IsNullOrWhiteSpace(model.PickupLocation) || string.IsNullOrWhiteSpace(model.DropoffLocation))
            {
                TempData["ErrorMessage"] = "Pickup and Dropoff locations are required.";
                return RedirectToAction(nameof(Index));
            }

            // Extract Origin & Destination preferences from form/model
            var originType = !string.IsNullOrWhiteSpace(model.OriginType) 
                ? model.OriginType 
                : (!string.IsNullOrWhiteSpace(Request.Form["NewDelivery.OriginType"].ToString()) 
                    ? Request.Form["NewDelivery.OriginType"].ToString() 
                    : "DoorstepPickup");

            var destType = !string.IsNullOrWhiteSpace(model.DestinationType) 
                ? model.DestinationType 
                : (!string.IsNullOrWhiteSpace(Request.Form["NewDelivery.DestinationType"].ToString()) 
                    ? Request.Form["NewDelivery.DestinationType"].ToString() 
                    : "DoorstepDelivery");

            // Detect cities and warehouse availability
            var originCity = WarehouseNetworkService.DetectCity(model.PickupLocation);
            var destCity = WarehouseNetworkService.DetectCity(model.DropoffLocation);
            bool hasOriginWh = WarehouseNetworkService.HasWarehouse(model.PickupLocation);
            bool hasDestWh = WarehouseNetworkService.HasWarehouse(model.DropoffLocation);
            var destWh = WarehouseNetworkService.GetWarehouse(model.DropoffLocation);

            // Extract Package Information, Quantities & Weights
            var packageName = !string.IsNullOrWhiteSpace(model.PackageName)
                ? model.PackageName.Trim()
                : Request.Form["NewDelivery.PackageName"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(packageName))
            {
                packageName = !string.IsNullOrWhiteSpace(model.ParcelDescription) ? model.ParcelDescription : "General Item";
            }

            int quantity = model.Quantity > 0 ? model.Quantity : 1;
            if (quantity <= 0 && int.TryParse(Request.Form["NewDelivery.Quantity"], out var qVal)) quantity = qVal;
            if (quantity <= 0) quantity = 1;

            double totalWeight = 0.0;
            if (double.TryParse(Request.Form["NewDelivery.ParcelWeight"], out var pwVal))
            {
                totalWeight = Math.Max(0.0, pwVal);
            }
            else if (model.ParcelWeight >= 0)
            {
                totalWeight = model.ParcelWeight;
            }
            else if (double.TryParse(Request.Form["ParcelWeight"], out var pwVal2))
            {
                totalWeight = Math.Max(0.0, pwVal2);
            }
            model.ParcelWeight = totalWeight;
            double singleItemWeight = quantity > 0 ? Math.Round(totalWeight / quantity, 2) : totalWeight;
            model.SingleItemWeight = singleItemWeight;

            // Dimensions & Cubic Feet (D2 in rate formula)
            string dimInput = Request.Form["DimensionsInput"].ToString();
            if (string.IsNullOrWhiteSpace(dimInput))
            {
                dimInput = Request.Form["NewDelivery.Dimensions"].ToString();
            }

            double length = 0.0;
            double width = 0.0;
            double height = 0.0;
            double totalCft = 0.0;

            if (!string.IsNullOrWhiteSpace(dimInput))
            {
                var dims = CostEstimationService.ParseDimensions(dimInput, quantity);
                length = dims.length;
                width = dims.width;
                height = dims.height;
                totalCft = dims.cft;
            }

            if (totalCft <= 0)
            {
                if (double.TryParse(Request.Form["NewDelivery.TotalCubicFeet"], out var cftForm) && cftForm > 0)
                {
                    totalCft = cftForm;
                }
                else
                {
                    double lVal = 0, wVal = 0, hVal = 0;
                    if (double.TryParse(Request.Form["NewDelivery.Length"], out lVal)) length = lVal;
                    else if (model.Length > 0) length = model.Length;

                    if (double.TryParse(Request.Form["NewDelivery.Width"], out wVal)) width = wVal;
                    else if (model.Width > 0) width = model.Width;

                    if (double.TryParse(Request.Form["NewDelivery.Height"], out hVal)) height = hVal;
                    else if (model.Height > 0) height = model.Height;

                    if (length > 0 && width > 0 && height > 0)
                    {
                        totalCft = Math.Round(length * width * height * (quantity > 0 ? quantity : 1), 2);
                    }
                }
            }

            model.Length = length;
            model.Width = width;
            model.Height = height;
            model.TotalCubicFeet = totalCft;

            var originWhForm = !string.IsNullOrWhiteSpace(model.OriginWarehouse)
                ? model.OriginWarehouse
                : Request.Form["NewDelivery.OriginWarehouse"].ToString();
            var destWhForm = !string.IsNullOrWhiteSpace(model.DestinationWarehouse)
                ? model.DestinationWarehouse
                : Request.Form["NewDelivery.DestinationWarehouse"].ToString();

            // Calculate AI Cost with locations & formula
            var costRequest = new CostEstimateRequest
            {
                PickupLocation = model.PickupLocation,
                DropoffLocation = model.DropoffLocation,
                OriginWarehouse = originWhForm,
                DestinationWarehouse = destWhForm,
                DistanceKm = model.EstimatedDistanceKm,
                ParcelWeight = totalWeight,
                SingleItemWeight = singleItemWeight,
                Quantity = quantity,
                TotalCubicFeet = totalCft,
                VehicleType = model.VehicleType,
                RouteType = model.RouteType,
                OriginType = originType,
                DestinationType = destType
            };

            var costEstimate = _costService.CalculateCost(costRequest);
            double distance = costEstimate.TotalDrivingDistanceKm > 0 ? costEstimate.TotalDrivingDistanceKm : (costEstimate.DistanceKm > 0 ? costEstimate.DistanceKm : 15.0);

            // Generate unique tracking number e.g. FLT-2026-001242
            var trackingNumber = $"FLT-{DateTime.UtcNow.Year}-{Random.Shared.Next(100000, 999999)}";

            var deliveryRequest = new DeliveryRequest
            {
                TrackingNumber = trackingNumber,
                UserID = userId,
                RequestDate = DateTime.UtcNow,
                PickupLocation = model.PickupLocation,
                DropoffLocation = model.DropoffLocation,
                ParcelWeight = totalWeight,
                SingleItemWeight = singleItemWeight,
                Length = length,
                Height = height,
                Width = width,
                PackageName = packageName,
                Quantity = quantity,
                TotalCubicFeet = totalCft,
                ParcelDescription = string.IsNullOrWhiteSpace(model.ParcelDescription) ? "Electronics" : model.ParcelDescription,
                VehicleType = model.VehicleType,
                RouteType = model.RouteType,
                DistanceKm = distance,
                RequestedStatus = "Pending",
                WeightStatus = "PendingVerification",
                EstimatedCost = costEstimate.EstimatedTotal,
                OriginType = originType,
                DestinationType = destType,
                OriginCity = originCity,
                DestinationCity = destCity,
                HasOriginWarehouse = hasOriginWh,
                HasDestinationWarehouse = hasDestWh,
                IsPrivateTransport = costEstimate.IsPrivateTransport,
                PrivateTransportSurcharge = costEstimate.PrivateTransportSurcharge,
                DestinationWarehouseName = destWh?.HubName,
                DestinationWarehouseAddress = destWh?.Address
            };

            _context.DeliveryRequests.Add(deliveryRequest);
            await _context.SaveChangesAsync();

            // Save AI Cost Record
            await _costService.SaveCostEstimateAsync(deliveryRequest.RequestID, costRequest, costEstimate);

            // Broadcast notification to Admins
            if (_notificationService != null)
            {
                var admins = await _context.Admins.ToListAsync();
                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Admin",
                        admin.UserID,
                        "New Delivery Request",
                        $"Customer booked delivery {deliveryRequest.TrackingNumber} from {deliveryRequest.PickupLocation} to {deliveryRequest.DropoffLocation} (Origin: {originType}, Dest: {destType}, Private: {(deliveryRequest.IsPrivateTransport ? "Yes" : "No")}).",
                        deliveryRequest.RequestID
                    );
                }

                // Customer notification regarding initial Pending status and warehouse arrival
                await _notificationService.CreateNotificationAsync(
                    "Customer",
                    userId,
                    "Delivery Request Submitted (Pending)",
                    $"Aap ki delivery request {trackingNumber} submit ho gayi hai (Status: Pending). Aap ka status jab saman warehouse me pohanch jaye ga to update ho jaye ga aur aap ko notification aa jaye ga.",
                    deliveryRequest.RequestID
                );
            }

            string extraNotice = deliveryRequest.IsPrivateTransport 
                ? " (Note: Special delivery surcharge (+15%) applied as warehouse is not in destination city)" 
                : "";

            TempData["SuccessMessage"] = $"Delivery request {trackingNumber} submitted successfully! Status: Pending. Aap ka status jab saman warehouse me pohanch jaye ga to update ho jaye ga aur aap ko notification aa jaye ga. Total: Rs. {costEstimate.EstimatedTotal:F0}{extraNotice} (Editable or cancellable within 1 hour).";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Customer/CancelDelivery
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelDelivery(int id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account", new { role = "Customer" });
            }

            var request = await _context.DeliveryRequests.FirstOrDefaultAsync(r => r.RequestID == id && r.UserID == userId);
            if (request == null)
            {
                TempData["ErrorMessage"] = "Delivery request not found.";
                return RedirectToAction(nameof(Index));
            }

            var hoursElapsed = (DateTime.UtcNow - request.RequestDate).TotalHours;
            if (hoursElapsed > 1.0)
            {
                TempData["ErrorMessage"] = "This delivery request cannot be cancelled because the 1-hour window has expired. It is now locked for shipping.";
                return RedirectToAction(nameof(Index));
            }

            if (request.RequestedStatus != "Pending")
            {
                TempData["ErrorMessage"] = $"Cannot cancel this request because its status is '{request.RequestedStatus}'.";
                return RedirectToAction(nameof(Index));
            }

            request.RequestedStatus = "Cancelled";
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Delivery request {request.TrackingNumber} has been successfully cancelled.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Customer/EditDelivery
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDelivery(int id, DeliveryRequestInputModel model)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account", new { role = "Customer" });
            }

            var request = await _context.DeliveryRequests.FirstOrDefaultAsync(r => r.RequestID == id && r.UserID == userId);
            if (request == null)
            {
                TempData["ErrorMessage"] = "Delivery request not found.";
                return RedirectToAction(nameof(Index));
            }

            var hoursElapsed = (DateTime.UtcNow - request.RequestDate).TotalHours;
            if (hoursElapsed > 1.0)
            {
                TempData["ErrorMessage"] = "This delivery request cannot be edited because the 1-hour window has expired. It is now locked for shipping.";
                return RedirectToAction(nameof(Index));
            }

            if (request.RequestedStatus != "Pending")
            {
                TempData["ErrorMessage"] = $"Cannot edit this request because its status is '{request.RequestedStatus}'.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(model.PickupLocation) || string.IsNullOrWhiteSpace(model.DropoffLocation))
            {
                TempData["ErrorMessage"] = "Pickup and Dropoff locations are required.";
                return RedirectToAction(nameof(Index));
            }

            int quantity = model.Quantity > 0 ? model.Quantity : (request.Quantity > 0 ? request.Quantity : 1);
            if (quantity <= 0 && int.TryParse(Request.Form["Quantity"], out var qEditParsed)) quantity = qEditParsed;
            if (quantity <= 0) quantity = 1;

            double singleItemWeight = model.SingleItemWeight > 0 ? model.SingleItemWeight : (request.SingleItemWeight > 0 ? request.SingleItemWeight : 2.0);
            if (double.TryParse(Request.Form["SingleItemWeight"], out var siwEdit) && siwEdit > 0) singleItemWeight = siwEdit;

            double totalWeight = Math.Round(singleItemWeight * quantity, 2);
            if (totalWeight <= 0) totalWeight = 2.0;

            double length = model.Length > 0 ? model.Length : (request.Length > 0 ? request.Length : 2.0);
            double height = model.Height > 0 ? model.Height : (request.Height > 0 ? request.Height : 1.0);
            double width = model.Width > 0 ? model.Width : (request.Width > 0 ? request.Width : 1.5);
            double totalCft = Math.Round(length * width * height * quantity, 2);

            var editPkgName = !string.IsNullOrWhiteSpace(model.PackageName)
                ? model.PackageName.Trim()
                : Request.Form["PackageName"].ToString().Trim();

            var vehicleType = !string.IsNullOrWhiteSpace(model.VehicleType) ? model.VehicleType : (!string.IsNullOrWhiteSpace(request.VehicleType) ? request.VehicleType : "Truck");

            var costRequest = new CostEstimateRequest
            {
                PickupLocation = model.PickupLocation,
                DropoffLocation = model.DropoffLocation,
                DistanceKm = model.EstimatedDistanceKm > 0 ? model.EstimatedDistanceKm : request.DistanceKm,
                ParcelWeight = totalWeight,
                SingleItemWeight = singleItemWeight,
                Quantity = quantity,
                TotalCubicFeet = totalCft,
                VehicleType = vehicleType,
                RouteType = model.RouteType ?? request.RouteType
            };
            var costEstimate = _costService.CalculateCost(costRequest);

            request.PickupLocation = model.PickupLocation;
            request.DropoffLocation = model.DropoffLocation;
            request.ParcelWeight = totalWeight;
            request.SingleItemWeight = singleItemWeight;
            request.Length = length;
            request.Height = height;
            request.Width = width;
            request.Quantity = quantity;
            request.TotalCubicFeet = totalCft;
            if (!string.IsNullOrWhiteSpace(editPkgName))
                request.PackageName = editPkgName;
            request.VehicleType = vehicleType;
            if (!string.IsNullOrWhiteSpace(model.RouteType))
                request.RouteType = model.RouteType;
            if (!string.IsNullOrWhiteSpace(model.ParcelDescription))
                request.ParcelDescription = model.ParcelDescription;
            if (!string.IsNullOrWhiteSpace(model.OriginType))
                request.OriginType = model.OriginType;
            if (!string.IsNullOrWhiteSpace(model.DestinationType))
                request.DestinationType = model.DestinationType;
            request.DistanceKm = costEstimate.DistanceKm > 0 ? costEstimate.DistanceKm : request.DistanceKm;
            request.EstimatedCost = costEstimate.EstimatedTotal;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Delivery request {request.TrackingNumber} has been updated successfully within your 1-hour window! New total: Rs. {Math.Floor(request.EstimatedCost):F0}";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Customer/SetDestinationDeliveryPreference
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDestinationDeliveryPreference(int requestId, string preference)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account", new { role = "Customer" });
            }

            var request = await _context.DeliveryRequests.FirstOrDefaultAsync(r => r.RequestID == requestId && r.UserID == userId);
            if (request == null)
            {
                TempData["ErrorMessage"] = "Delivery request not found.";
                return RedirectToAction(nameof(Index));
            }

            if (preference == "WarehousePickup" || preference == "DoorstepDelivery")
            {
                request.DestinationType = preference;
                await _context.SaveChangesAsync();

                string prefText = preference == "WarehousePickup" 
                    ? "Self-Pickup from Destination Warehouse" 
                    : "Doorstep Delivery to your Address";

                // Notify Admin
                var admins = await _context.Admins.ToListAsync();
                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Admin",
                        admin.UserID,
                        "Customer Delivery Preference Updated",
                        $"Customer selected '{prefText}' for shipment {request.TrackingNumber}.",
                        request.RequestID
                    );
                }

                TempData["SuccessMessage"] = $"Your delivery preference for shipment {request.TrackingNumber} has been updated to '{prefText}'!";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: /Customer/MeasureAddressDistance
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> MeasureAddressDistance(string city, string address, string legType)
        {
            if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(address))
            {
                return Json(new { success = false, distanceKm = 0.0 });
            }

            var hub = WarehouseNetworkService.GetWarehouse(city);
            string hubAddress = hub?.Address ?? $"{city} Central Hub, Pakistan";

            var routeResult = await _mapRoutingService.GetDrivingDistanceAsync(address, hubAddress);
            double dist = routeResult.Success && routeResult.DistanceKm > 0 ? routeResult.DistanceKm : 0.0;

            return Json(new
            {
                success = true,
                distanceKm = Math.Round(dist, 1),
                durationMinutes = routeResult.DurationMinutes,
                hubAddress = hubAddress,
                coords = routeResult.OriginLat.HasValue && routeResult.OriginLng.HasValue 
                    ? new[] { routeResult.OriginLat.Value, routeResult.OriginLng.Value } 
                    : null,
                provider = routeResult.Provider
            });
        }

        // GET: /Customer/Track
        public async Task<IActionResult> Track(string? trackingNumber)
        {
            if (string.IsNullOrWhiteSpace(trackingNumber))
            {
                return RedirectToAction(nameof(Index));
            }

            var delivery = await _context.DeliveryRequests
                .Include(r => r.User)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Driver)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.Vehicle)
                .Include(r => r.Assignment)
                    .ThenInclude(a => a!.StatusUpdates)
                .FirstOrDefaultAsync(r => r.TrackingNumber == trackingNumber.Trim());

            if (delivery == null)
            {
                TempData["ErrorMessage"] = $"Tracking number '{trackingNumber}' was not found in the system.";
                return RedirectToAction(nameof(Index));
            }

            return View(delivery);
        }
    }
}
