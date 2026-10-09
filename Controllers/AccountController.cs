using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _memoryCache;
        private readonly IConfiguration _config;

        public AccountController(
            IAuthService authService,
            IEmailService emailService,
            IMemoryCache memoryCache,
            IConfiguration config)
        {
            _authService = authService;
            _emailService = emailService;
            _memoryCache = memoryCache;
            _config = config;
        }

        // GET: /Account/SelectRole (Figure 18)
        [HttpGet]
        public IActionResult SelectRole()
        {
            return RedirectToAction("Index", "Home");
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

            // Auto-detect user role by email so no manual Customer/Admin selection is needed
            var roleToAuthenticate = model.Role;
            var detectedRole = await _authService.GetUserRoleByEmailAsync(model.Email);
            if (!string.IsNullOrEmpty(detectedRole))
            {
                roleToAuthenticate = detectedRole;
            }

            var user = await _authService.AuthenticateAsync(model.Email, model.Password, roleToAuthenticate ?? "Customer");
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid email address or password. Please check your credentials and try again.");
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
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddDays(1)
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

        // POST: /Account/SendVerificationOtp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendVerificationOtp([FromBody] SendOtpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return Json(new { success = false, message = "Please provide a valid email address." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(normalizedEmail))
            {
                return Json(new { success = false, message = "Invalid email address format." });
            }

            if (await _authService.UserExistsAsync(normalizedEmail))
            {
                return Json(new { success = false, message = "An account with this email address already exists. Please sign in instead." });
            }

            // Rate limit check: 30 seconds
            var lastSentKey = $"OtpLastSent_{normalizedEmail}";
            if (_memoryCache.TryGetValue(lastSentKey, out DateTime lastSent) && (DateTime.UtcNow - lastSent).TotalSeconds < 30)
            {
                var waitSec = 30 - (int)(DateTime.UtcNow - lastSent).TotalSeconds;
                return Json(new { success = false, message = $"Please wait {waitSec} seconds before requesting another verification code." });
            }

            var otp = Random.Shared.Next(100000, 999999).ToString();
            var cacheKey = $"RegisterOtp_{normalizedEmail}";
            _memoryCache.Set(cacheKey, otp, TimeSpan.FromMinutes(10));
            _memoryCache.Set(lastSentKey, DateTime.UtcNow, TimeSpan.FromSeconds(30));
            HttpContext.Session.SetString(cacheKey, otp);

            string subject = $"Fleetify: {otp} is your Email Verification Code";
            string body = $@"Dear User,

Welcome to Fleetify Logistics Platform!

Please use the following 6-digit OTP code to verify your email address and complete your account creation:

================================
    VERIFICATION CODE: {otp}
================================

This verification code is valid for 10 minutes.

Please enter this code on the registration page to confirm your email and enable account creation.

If you did not request this verification code, you can safely disregard this email.

Best regards,
Fleetify Operations & Logistics Team";

            var emailSent = await _emailService.SendEmailAsync(normalizedEmail, subject, body);

            var smtpPass = _config["EmailSettings:Password"];
            bool isRealSmtp = !string.IsNullOrWhiteSpace(smtpPass) && emailSent;
            string userMessage = isRealSmtp
                ? $"A 6-digit verification code has been dispatched to your email inbox ({normalizedEmail})."
                : $"Email delivery encountered a temporary network delay. Your verification code is: {otp}";

            return Json(new { 
                success = true, 
                message = userMessage,
                devOtp = isRealSmtp ? null : otp
            });
        }

        // POST: /Account/VerifyOtp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Otp))
            {
                return Json(new { success = false, message = "Email and OTP code are required." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var cacheKey = $"RegisterOtp_{normalizedEmail}";

            string? expectedOtp = null;
            if (_memoryCache.TryGetValue(cacheKey, out string? cached))
            {
                expectedOtp = cached;
            }
            else
            {
                expectedOtp = HttpContext.Session.GetString(cacheKey);
            }

            if (expectedOtp != null && expectedOtp == request.Otp.Trim())
            {
                _memoryCache.Remove(cacheKey);
                HttpContext.Session.Remove(cacheKey);

                var verifiedKey = $"EmailVerified_{normalizedEmail}";
                _memoryCache.Set(verifiedKey, true, TimeSpan.FromMinutes(30));
                HttpContext.Session.SetString("VerifiedEmail", normalizedEmail);

                return Json(new { success = true, message = "Email verified successfully!" });
            }

            return Json(new { success = false, message = "Invalid or expired verification code. Please check and try again." });
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

            var normalizedEmail = model.Email?.Trim().ToLowerInvariant() ?? "";
            var sessionVerified = HttpContext.Session.GetString("VerifiedEmail");
            var isVerified = (sessionVerified != null && sessionVerified == normalizedEmail)
                             || (_memoryCache.TryGetValue($"EmailVerified_{normalizedEmail}", out bool cacheVerified) && cacheVerified);

            if (!isVerified)
            {
                ModelState.AddModelError("Email", "Please verify your email address using the OTP code before creating your account.");
                return View(model);
            }

            if (await _authService.UserExistsAsync(normalizedEmail))
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

            // Clear session verification after successful registration
            HttpContext.Session.Remove("VerifiedEmail");
            _memoryCache.Remove($"EmailVerified_{normalizedEmail}");

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

        // POST: /Account/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return Json(new { success = false, message = "Please provide your registered email address." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(normalizedEmail))
            {
                return Json(new { success = false, message = "Invalid email address format." });
            }

            var exists = await _authService.UserExistsAsync(normalizedEmail);
            if (!exists)
            {
                return Json(new { success = false, message = "No account found with this email address. Please check your email or sign up." });
            }

            // Rate limit check: 30 seconds
            var lastSentKey = $"ForgotPwdLastSent_{normalizedEmail}";
            if (_memoryCache.TryGetValue(lastSentKey, out DateTime lastSent) && (DateTime.UtcNow - lastSent).TotalSeconds < 30)
            {
                var waitSec = 30 - (int)(DateTime.UtcNow - lastSent).TotalSeconds;
                return Json(new { success = false, message = $"Please wait {waitSec} seconds before requesting another reset email." });
            }

            var token = Guid.NewGuid().ToString("N");
            var otp = Random.Shared.Next(100000, 999999).ToString();

            var tokenCacheKey = $"ResetPasswordToken_{token}";
            _memoryCache.Set(tokenCacheKey, normalizedEmail, TimeSpan.FromMinutes(30));
            _memoryCache.Set($"ResetPasswordOtp_{normalizedEmail}", otp, TimeSpan.FromMinutes(30));
            _memoryCache.Set(lastSentKey, DateTime.UtcNow, TimeSpan.FromSeconds(30));

            var resetLink = $"{Request.Scheme}://{Request.Host}/Account/ResetPassword?email={Uri.EscapeDataString(normalizedEmail)}&token={token}";

            string subject = "Fleetify: Reset Your Password";
            string body = $@"Dear User,

We received a request to reset the password for your Fleetify Logistics account ({normalizedEmail}).

Click the link below to securely reset your password:
{resetLink}

--------------------------------------------------
Alternatively, your verification code is: {otp}
--------------------------------------------------

This password reset link is valid for 30 minutes.

If you did not make this request, you can safely ignore this email; your account remains completely secure.

Best regards,
Fleetify Operations & Logistics Team
Support: fypfleetify@gmail.com";

            var emailSent = await _emailService.SendEmailAsync(normalizedEmail, subject, body);

            return Json(new {
                success = true,
                message = "Password reset instructions and link have been dispatched to your email inbox! Please check your email.",
                devLink = emailSent ? null : resetLink
            });
        }

        // GET: /Account/ResetPassword
        [HttpGet]
        public IActionResult ResetPassword(string? email, string? token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Invalid or expired password reset link.";
                return RedirectToAction("Login");
            }

            var normalizedEmail = email.Trim().ToLowerInvariant();
            var tokenCacheKey = $"ResetPasswordToken_{token}";

            if (!_memoryCache.TryGetValue(tokenCacheKey, out string? cachedEmail) || cachedEmail != normalizedEmail)
            {
                TempData["ErrorMessage"] = "This password reset link has expired or is invalid. Please request a new one.";
                return RedirectToAction("Login");
            }

            var model = new ResetPasswordViewModel
            {
                Email = normalizedEmail,
                Token = token
            };

            return View(model);
        }

        // POST: /Account/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email?.Trim().ToLowerInvariant() ?? "";
            var tokenCacheKey = $"ResetPasswordToken_{model.Token}";

            if (!_memoryCache.TryGetValue(tokenCacheKey, out string? cachedEmail) || cachedEmail != normalizedEmail)
            {
                ModelState.AddModelError(string.Empty, "This password reset session has expired. Please request a new link.");
                return View(model);
            }

            var success = await _authService.ResetPasswordAsync(normalizedEmail, model.NewPassword);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, "Unable to reset password. User account not found.");
                return View(model);
            }

            _memoryCache.Remove(tokenCacheKey);
            _memoryCache.Remove($"ResetPasswordOtp_{normalizedEmail}");

            TempData["SuccessMessage"] = "Your password has been successfully reset! Please sign in with your new password.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
