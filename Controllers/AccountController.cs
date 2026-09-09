using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;

        public AccountController(IAuthService authService)
        {
            _authService = authService;
        }

        // GET: /Account/SelectRole (Figure 18)
        [HttpGet]
        public IActionResult SelectRole()
        {
            return View();
        }

        // GET: /Account/Login (Figure 20)
        [HttpGet]
        public IActionResult Login(string? role = "Customer", string? returnUrl = null)
        {
            var model = new LoginViewModel
            {
                Role = string.IsNullOrEmpty(role) ? "Customer" : role,
                ReturnUrl = returnUrl
            };
            return View(model);
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _authService.AuthenticateAsync(model.Email, model.Password, model.Role);
            if (user == null)
            {
                // Check if user is registered under a different role
                var actualRole = await _authService.GetUserRoleByEmailAsync(model.Email);
                if (!string.IsNullOrEmpty(actualRole) && !actualRole.Equals(model.Role, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(string.Empty,
                        $"This email is registered as '{actualRole}', not as '{model.Role}'. Please select the '{actualRole}' login option.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, $"Invalid email or password for {model.Role} role.");
                }
                return View(model);
            }

            // If already authenticated with another account/role, sign out first
            if (User.Identity?.IsAuthenticated == true)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            // Generate JWT Token
            var jwtToken = _authService.GenerateJwtToken(user);

            // Establish Cookie-based authentication session for MVC views
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("JwtToken", jwtToken)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties
            );

            // Set cookie for client-side API calls if needed
            Response.Cookies.Append("fleetify_jwt", jwtToken, new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = false,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return user.Role switch
            {
                "Admin" => RedirectToAction("Index", "Admin"),
                "Driver" => RedirectToAction("Index", "Driver"),
                _ => RedirectToAction("Index", "Customer")
            };
        }

        // GET: /Account/Register (Figure 19)
        [HttpGet]
        public IActionResult Register(string? role = "Customer")
        {
            var model = new RegisterViewModel
            {
                Role = string.IsNullOrEmpty(role) ? "Customer" : role
            };
            return View(model);
        }

        // POST: /Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (await _authService.UserExistsAsync(model.Email))
            {
                ModelState.AddModelError("Email", "An account with this email address already exists.");
                return View(model);
            }

            if (model.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                await _authService.RegisterAdminAsync(model);
            }
            else if (model.Role.Equals("Driver", StringComparison.OrdinalIgnoreCase))
            {
                await _authService.RegisterDriverAsync(model);
            }
            else
            {
                await _authService.RegisterCustomerAsync(model);
            }

            TempData["SuccessMessage"] = "Account registered successfully! Please log in.";
            return RedirectToAction("Login", new { role = model.Role });
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete("fleetify_jwt");
            return RedirectToAction("SelectRole", "Account");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
