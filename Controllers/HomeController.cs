using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Fleetify.Data;
using Fleetify.Models.ViewModels;

namespace Fleetify.Controllers
{
    public class HomeController : Controller
    {
        private readonly FleetifyDbContext _context;

        public HomeController(FleetifyDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var role = User.FindFirstValue(ClaimTypes.Role);
                return role switch
                {
                    "Admin" => RedirectToAction("Index", "Admin"),
                    "Driver" => RedirectToAction("Index", "Driver"),
                    _ => RedirectToAction("Index", "Customer")
                };
            }

            var model = new HomeViewModel();
            try
            {
                var totalVehicles = await _context.Vehicles.CountAsync();
                var totalRequests = await _context.DeliveryRequests.CountAsync();
                var deliveredCount = await _context.DeliveryRequests.CountAsync(d => d.RequestedStatus == "Delivered");

                if (totalVehicles > 0)
                {
                    model.ActiveFleets = totalVehicles >= 50 ? totalVehicles : 100;
                }
                if (totalRequests > 0)
                {
                    model.TotalShipments = totalRequests >= 100 ? totalRequests : 1500;
                    if (totalRequests > 0)
                    {
                        var rate = Math.Round((double)deliveredCount / totalRequests * 100, 1);
                        if (rate >= 90 && rate <= 100)
                        {
                            model.DeliverySuccessRate = rate;
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback to default values
            }

            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
