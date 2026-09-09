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
    [Authorize(Roles = "Customer")]
    public class CustomerController : Controller
    {
        private readonly FleetifyDbContext _context;
        private readonly ICostEstimationService _costService;
        private readonly INotificationService _notificationService;

        public CustomerController(
            FleetifyDbContext context,
            ICostEstimationService costService,
            INotificationService notificationService)
        {
            _context = context;
            _costService = costService;
            _notificationService = notificationService;
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

            var viewModel = new CustomerDashboardViewModel
            {
                UserId = user.UserID,
                UserName = user.FullName,
                TotalDeliveries = totalDeliveries,
                InTransitCount = inTransit,
                DeliveredCount = delivered,
                TotalSpent = Math.Round(totalSpent, 2),
                MyDeliveries = deliveries
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
            if (model.ParcelWeight <= 0)
            {
                if (double.TryParse(Request.Form["NewDelivery.ParcelWeight"], out var w) || double.TryParse(Request.Form["ParcelWeight"], out w))
                {
                    model.ParcelWeight = w;
                }
                else
                {
                    model.ParcelWeight = 2.0;
                }
            }
            if (string.IsNullOrWhiteSpace(model.VehicleType))
            {
                model.VehicleType = Request.Form["NewDelivery.VehicleType"].ToString();
                if (string.IsNullOrWhiteSpace(model.VehicleType)) model.VehicleType = "Van";
            }
            if (string.IsNullOrWhiteSpace(model.RouteType))
            {
                model.RouteType = Request.Form["NewDelivery.RouteType"].ToString();
                if (string.IsNullOrWhiteSpace(model.RouteType)) model.RouteType = "Standard";
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

            // Calculate AI Cost with locations
            var costRequest = new CostEstimateRequest
            {
                PickupLocation = model.PickupLocation,
                DropoffLocation = model.DropoffLocation,
                DistanceKm = model.EstimatedDistanceKm,
                ParcelWeight = model.ParcelWeight,
                VehicleType = model.VehicleType,
                RouteType = model.RouteType
            };

            var costEstimate = _costService.CalculateCost(costRequest);
            double distance = costEstimate.DistanceKm > 0 ? costEstimate.DistanceKm : 15.0;

            // Generate unique tracking number e.g. FLT-2026-001242
            var trackingNumber = $"FLT-{DateTime.UtcNow.Year}-{Random.Shared.Next(100000, 999999)}";

            var deliveryRequest = new DeliveryRequest
            {
                TrackingNumber = trackingNumber,
                UserID = userId,
                RequestDate = DateTime.UtcNow,
                PickupLocation = model.PickupLocation,
                DropoffLocation = model.DropoffLocation,
                ParcelWeight = model.ParcelWeight,
                ParcelDescription = string.IsNullOrWhiteSpace(model.ParcelDescription) ? "General Package" : model.ParcelDescription,
                VehicleType = model.VehicleType,
                RouteType = model.RouteType,
                DistanceKm = distance,
                RequestedStatus = "Pending",
                EstimatedCost = costEstimate.EstimatedTotal
            };

            _context.DeliveryRequests.Add(deliveryRequest);
            await _context.SaveChangesAsync();

            // Save AI Cost Record
            await _costService.SaveCostEstimateAsync(deliveryRequest.RequestID, costRequest, costEstimate);

            // Notify Admin
            var admins = await _context.Admins.ToListAsync();
            foreach (var admin in admins)
            {
                await _notificationService.CreateNotificationAsync(
                    "Admin",
                    admin.UserID,
                    "New Delivery Request",
                    $"Customer booked delivery {deliveryRequest.TrackingNumber} from {deliveryRequest.PickupLocation} to {deliveryRequest.DropoffLocation}.",
                    deliveryRequest.RequestID
                );
            }

            TempData["SuccessMessage"] = $"Delivery request submitted successfully! Your tracking number is {trackingNumber}. Total: ${costEstimate.EstimatedTotal:F2}";
            return RedirectToAction(nameof(Index));
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
