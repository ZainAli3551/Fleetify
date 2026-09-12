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
    [Authorize(Roles = "Driver")]
    public class DriverController : Controller
    {
        private readonly FleetifyDbContext _context;
        private readonly INotificationService _notificationService;

        public DriverController(FleetifyDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        // GET: /Driver (Figure 22: Driver Portal)
        public async Task<IActionResult> Index()
        {
            var driverIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(driverIdStr, out int driverId))
            {
                return RedirectToAction("Login", "Account", new { role = "Driver" });
            }

            var driver = await _context.Drivers
                .Include(d => d.Assignments)
                    .ThenInclude(a => a.DeliveryRequest)
                        .ThenInclude(r => r!.User)
                .Include(d => d.Assignments)
                    .ThenInclude(a => a.Vehicle)
                .FirstOrDefaultAsync(d => d.UserID == driverId);

            if (driver == null)
            {
                return RedirectToAction("Login", "Account", new { role = "Driver" });
            }

            var allAssignments = driver.Assignments.ToList();

            var available = allAssignments.Where(a => a.AssignmentStatus == "Pending").ToList();
            var active = allAssignments.Where(a => a.AssignmentStatus == "Accepted" || a.AssignmentStatus == "In-Progress").ToList();
            var completed = allAssignments.Where(a => a.AssignmentStatus == "Completed").ToList();

            var today = DateTime.UtcNow.Date;
            var todaysDeliveries = allAssignments.Count(a => a.AssignedDate >= today);
            var completedCount = completed.Count;
            var distanceToday = allAssignments.Where(a => a.AssignedDate >= today).Sum(a => a.DeliveryRequest?.DistanceKm ?? 0);
            var earningsToday = allAssignments.Where(a => a.AssignedDate >= today && a.AssignmentStatus == "Completed")
                                              .Sum(a => (a.DeliveryRequest?.EstimatedCost ?? 0) * 0.70); // 70% driver share

            var viewModel = new DriverDashboardViewModel
            {
                DriverId = driver.UserID,
                DriverName = driver.FullName,
                LicenseNumber = driver.LicenseNumber,
                AvailabilityStatus = driver.AvailabilityStatus,
                TodaysDeliveries = todaysDeliveries,
                CompletedDeliveries = completedCount,
                DistanceTodayKm = Math.Round(distanceToday, 1),
                EarningsToday = Math.Round(earningsToday, 2),
                AvailableAssignments = available,
                ActiveAssignments = active,
                CompletedAssignments = completed
            };

            return View(viewModel);
        }

        // POST: /Driver/AcceptAssignment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptAssignment(int assignmentId)
        {
            var assignment = await _context.Assignments
                .Include(a => a.DeliveryRequest)
                .FirstOrDefaultAsync(a => a.AssignmentID == assignmentId);

            if (assignment != null)
            {
                assignment.AssignmentStatus = "Accepted";
                if (assignment.DeliveryRequest != null)
                {
                    assignment.DeliveryRequest.RequestedStatus = "In-Transit";
                }

                _context.StatusUpdates.Add(new StatusUpdate
                {
                    AssignmentID = assignment.AssignmentID,
                    DriverID = assignment.DriverID,
                    UpdateStatus = "Accepted",
                    UpdateTime = DateTime.UtcNow,
                    Remarks = "Driver accepted shipment and is en route for pickup."
                });

                await _context.SaveChangesAsync();

                if (assignment.DeliveryRequest != null)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Customer",
                        assignment.DeliveryRequest.UserID,
                        "Delivery Accepted",
                        $"Driver has accepted your shipment {assignment.DeliveryRequest.TrackingNumber} and is heading to the pickup location.",
                        assignment.RequestID
                    );
                }

                TempData["SuccessMessage"] = "Delivery assignment accepted! It is now active.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Driver/RejectAssignment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectAssignment(int assignmentId)
        {
            var assignment = await _context.Assignments
                .Include(a => a.DeliveryRequest)
                .Include(a => a.Driver)
                .FirstOrDefaultAsync(a => a.AssignmentID == assignmentId);

            if (assignment != null)
            {
                assignment.AssignmentStatus = "Rejected";
                if (assignment.DeliveryRequest != null)
                {
                    assignment.DeliveryRequest.RequestedStatus = "Pending";
                }

                if (assignment.Driver != null)
                {
                    assignment.Driver.AvailabilityStatus = "Available";
                }

                await _context.SaveChangesAsync();
                TempData["InfoMessage"] = "Delivery assignment rejected. Admin will reassign.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Driver/UpdateDeliveryStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDeliveryStatus(int assignmentId, string status, string remarks)
        {
            var assignment = await _context.Assignments
                .Include(a => a.DeliveryRequest)
                .Include(a => a.Driver)
                .Include(a => a.Vehicle)
                .FirstOrDefaultAsync(a => a.AssignmentID == assignmentId);

            if (assignment != null)
            {
                var update = new StatusUpdate
                {
                    AssignmentID = assignment.AssignmentID,
                    DriverID = assignment.DriverID,
                    UpdateStatus = status,
                    UpdateTime = DateTime.UtcNow,
                    Remarks = string.IsNullOrWhiteSpace(remarks) ? $"Status transitioned to {status}" : remarks
                };

                _context.StatusUpdates.Add(update);

                if (status == "Delivered")
                {
                    assignment.AssignmentStatus = "Completed";
                    if (assignment.DeliveryRequest != null)
                    {
                        assignment.DeliveryRequest.RequestedStatus = "Delivered";
                    }

                    if (assignment.Driver != null)
                    {
                        assignment.Driver.AvailabilityStatus = "Available";
                    }

                    if (assignment.Vehicle != null)
                    {
                        assignment.Vehicle.AvailabilityStatus = "Available";
                        assignment.Vehicle.CurrentMileage += assignment.DeliveryRequest?.DistanceKm ?? 10;
                    }
                }
                else
                {
                    assignment.AssignmentStatus = "In-Progress";
                    if (assignment.DeliveryRequest != null)
                    {
                        assignment.DeliveryRequest.RequestedStatus = status;
                    }
                }

                await _context.SaveChangesAsync();

                if (assignment.DeliveryRequest != null)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Customer",
                        assignment.DeliveryRequest.UserID,
                        $"Delivery Update: {status}",
                        $"Shipment {assignment.DeliveryRequest.TrackingNumber} status updated to '{status}'. {remarks}",
                        assignment.RequestID
                    );
                }

                TempData["SuccessMessage"] = $"Delivery updated to '{status}' successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Driver/VerifyWeight
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyWeight(int assignmentId, double verifiedWeight, string? remarks)
        {
            var assignment = await _context.Assignments
                .Include(a => a.DeliveryRequest)
                .Include(a => a.Driver)
                .FirstOrDefaultAsync(a => a.AssignmentID == assignmentId);

            if (assignment == null || assignment.DeliveryRequest == null)
            {
                TempData["ErrorMessage"] = "Assignment or Delivery Request not found.";
                return RedirectToAction(nameof(Index));
            }

            var req = assignment.DeliveryRequest;
            var driverName = assignment.Driver?.FullName ?? "Driver";

            if (verifiedWeight <= 0)
            {
                TempData["ErrorMessage"] = "Please enter a valid parcel weight in kg.";
                return RedirectToAction(nameof(Index));
            }

            req.VerifiedWeight = verifiedWeight;
            req.DriverVerificationNotes = remarks;

            bool isExactMatch = Math.Abs(verifiedWeight - req.ParcelWeight) < 0.01;

            if (isExactMatch)
            {
                req.WeightStatus = "VerifiedMatched";
                req.RequestedStatus = "Weight Verified";

                _context.StatusUpdates.Add(new StatusUpdate
                {
                    AssignmentID = assignment.AssignmentID,
                    DriverID = assignment.DriverID,
                    UpdateStatus = "Weight Verified",
                    UpdateTime = DateTime.UtcNow,
                    Remarks = string.IsNullOrWhiteSpace(remarks)
                        ? $"Driver verified weight upon pickup: {verifiedWeight:F1} kg (Matches declared weight)."
                        : $"Driver verified weight: {verifiedWeight:F1} kg (Matches declared). Note: {remarks}"
                });

                // Notify Admins
                var admins = await _context.Admins.ToListAsync();
                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Admin",
                        admin.UserID,
                        "Parcel Weight Verified",
                        $"Driver {driverName} verified exact weight ({verifiedWeight:F1} kg) for delivery {req.TrackingNumber}. Ready for finalization.",
                        req.RequestID
                    );
                }

                TempData["SuccessMessage"] = $"Weight verified successfully ({verifiedWeight:F1} kg matches declared weight). Admin has been notified to finalize cost.";
            }
            else
            {
                req.WeightStatus = "DiscrepancyReported";
                req.RequestedStatus = "Weight Reported";

                _context.StatusUpdates.Add(new StatusUpdate
                {
                    AssignmentID = assignment.AssignmentID,
                    DriverID = assignment.DriverID,
                    UpdateStatus = "Weight Discrepancy",
                    UpdateTime = DateTime.UtcNow,
                    Remarks = string.IsNullOrWhiteSpace(remarks)
                        ? $"Driver reported actual weight: {verifiedWeight:F1} kg (Customer declared: {req.ParcelWeight:F1} kg)."
                        : $"Driver reported actual weight: {verifiedWeight:F1} kg (Declared: {req.ParcelWeight:F1} kg). Note: {remarks}"
                });

                // Notify Admins
                var admins = await _context.Admins.ToListAsync();
                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Admin",
                        admin.UserID,
                        "Weight Discrepancy Reported",
                        $"Driver {driverName} reported weight discrepancy for {req.TrackingNumber}: Measured {verifiedWeight:F1} kg vs Declared {req.ParcelWeight:F1} kg. Admin action required to finalize cost.",
                        req.RequestID
                    );
                }

                TempData["WarningMessage"] = $"Weight discrepancy reported ({verifiedWeight:F1} kg vs declared {req.ParcelWeight:F1} kg). Admin has been notified to finalize cost.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
