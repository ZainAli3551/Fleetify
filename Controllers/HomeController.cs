using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Fleetify.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
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

            return RedirectToAction("SelectRole", "Account");
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
