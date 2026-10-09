using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Fleetify.Data;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly FleetifyDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(FleetifyDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword)) return false;

            // Direct match check in case plain text password was set manually in DB
            if (password == hashedPassword) return true;

            try
            {
                if (BCrypt.Net.BCrypt.Verify(password, hashedPassword))
                {
                    return true;
                }
            }
            catch
            {
                // Fallback check
            }

            return password == hashedPassword;
        }

        public string GenerateJwtToken(BaseUser user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? "DefaultFallbackSecretKeyThatIsAtLeast32BytesLong!";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "FleetifyApp",
                audience: _configuration["Jwt:Audience"] ?? "FleetifyUsers",
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<BaseUser?> AuthenticateAsync(string emailOrUsername, string password, string role)
        {
            var raw = (emailOrUsername ?? string.Empty).Trim();
            var input = raw.ToLower();

            // Build candidate email/usernames
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input };
            if (input == "admin") candidates.Add("admin@fleetify.com");
            if (input == "customer") candidates.Add("customer@fleetify.com");
            if (input == "driver") candidates.Add("driver@fleetify.com");
            if (!input.Contains("@")) candidates.Add($"{input}@fleetify.com");

            BaseUser? user = null;

            // 1. Try to find user in requested role table first
            var roleLower = (role ?? string.Empty).ToLower();
            if (roleLower == "admin")
            {
                user = await _context.Admins.FirstOrDefaultAsync(a => candidates.Contains(a.Email.ToLower()) || a.FullName.ToLower() == input);
            }
            else if (roleLower == "driver")
            {
                user = await _context.Drivers.FirstOrDefaultAsync(d => candidates.Contains(d.Email.ToLower()) || d.FullName.ToLower() == input);
            }
            else if (roleLower == "customer")
            {
                user = await _context.Users.FirstOrDefaultAsync(u => candidates.Contains(u.Email.ToLower()) || u.FullName.ToLower() == input);
            }

            // 2. If not found in requested role, search across all 3 tables seamlessly
            if (user == null)
            {
                user = await _context.Admins.FirstOrDefaultAsync(a => candidates.Contains(a.Email.ToLower()) || a.FullName.ToLower() == input);
                user ??= await _context.Drivers.FirstOrDefaultAsync(d => candidates.Contains(d.Email.ToLower()) || d.FullName.ToLower() == input);
                user ??= await _context.Users.FirstOrDefaultAsync(u => candidates.Contains(u.Email.ToLower()) || u.FullName.ToLower() == input);
            }

            // 3. Verify password
            if (user != null && VerifyPassword(password, user.PasswordHash))
            {
                return user;
            }

            return null;
        }

        public async Task<string?> GetUserRoleByEmailAsync(string emailOrUsername)
        {
            var raw = (emailOrUsername ?? string.Empty).Trim();
            var input = raw.ToLower();

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input };
            if (input == "admin") candidates.Add("admin@fleetify.com");
            if (input == "customer") candidates.Add("customer@fleetify.com");
            if (input == "driver") candidates.Add("driver@fleetify.com");
            if (!input.Contains("@")) candidates.Add($"{input}@fleetify.com");

            if (await _context.Admins.AnyAsync(a => candidates.Contains(a.Email.ToLower()) || a.FullName.ToLower() == input)) return "Admin";
            if (await _context.Drivers.AnyAsync(d => candidates.Contains(d.Email.ToLower()) || d.FullName.ToLower() == input)) return "Driver";
            if (await _context.Users.AnyAsync(u => candidates.Contains(u.Email.ToLower()) || u.FullName.ToLower() == input)) return "Customer";
            return null;
        }

        public async Task<bool> UserExistsAsync(string email)
        {
            email = email.Trim().ToLower();
            return await _context.Users.AnyAsync(u => u.Email.ToLower() == email)
                || await _context.Admins.AnyAsync(a => a.Email.ToLower() == email)
                || await _context.Drivers.AnyAsync(d => d.Email.ToLower() == email);
        }

        public async Task<User> RegisterCustomerAsync(RegisterViewModel model)
        {
            var user = new User
            {
                FullName = model.FullName,
                Email = model.Email.Trim().ToLower(),
                PasswordHash = HashPassword(model.Password),
                PhoneNumber = model.PhoneNumber,
                Address = !string.IsNullOrWhiteSpace(model.Address) ? model.Address : "Pakistan",
                Role = "Customer",
                AccountStatus = "Active",
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<Driver> RegisterDriverAsync(RegisterViewModel model)
        {
            var driver = new Driver
            {
                FullName = model.FullName,
                Email = model.Email.Trim().ToLower(),
                PasswordHash = HashPassword(model.Password),
                PhoneNumber = model.PhoneNumber,
                Role = "Driver",
                LicenseNumber = model.LicenseNumber ?? $"LIC-{Random.Shared.Next(10000, 99999)}",
                AvailabilityStatus = "Available",
                DriverType = !string.IsNullOrWhiteSpace(model.DriverType) ? model.DriverType : "Company",
                BackupContactNotes = model.BackupContactNotes,
                VehicleOwned = model.VehicleOwned,
                CreatedAt = DateTime.UtcNow
            };

            _context.Drivers.Add(driver);
            await _context.SaveChangesAsync();
            return driver;
        }

        public async Task<Admin> RegisterAdminAsync(RegisterViewModel model)
        {
            var admin = new Admin
            {
                FullName = model.FullName,
                Email = model.Email.Trim().ToLower(),
                PasswordHash = HashPassword(model.Password),
                PhoneNumber = model.PhoneNumber,
                Role = "Admin",
                CreatedAt = DateTime.UtcNow
            };

            _context.Admins.Add(admin);
            await _context.SaveChangesAsync();
            return admin;
        }

        public async Task<BaseUser?> FindUserByEmailOrUsernameAsync(string emailOrUsername)
        {
            var raw = (emailOrUsername ?? string.Empty).Trim();
            var input = raw.ToLower();

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input };
            if (input == "admin") candidates.Add("admin@fleetify.com");
            if (input == "customer") candidates.Add("customer@fleetify.com");
            if (input == "driver") candidates.Add("driver@fleetify.com");
            if (!input.Contains("@")) candidates.Add($"{input}@fleetify.com");

            BaseUser? user = await _context.Admins.FirstOrDefaultAsync(a => candidates.Contains(a.Email.ToLower()) || a.FullName.ToLower() == input);
            user ??= await _context.Drivers.FirstOrDefaultAsync(d => candidates.Contains(d.Email.ToLower()) || d.FullName.ToLower() == input);
            user ??= await _context.Users.FirstOrDefaultAsync(u => candidates.Contains(u.Email.ToLower()) || u.FullName.ToLower() == input);
            return user;
        }

        public async Task<bool> ResetPasswordAsync(string emailOrUsername, string newPassword)
        {
            var user = await FindUserByEmailOrUsernameAsync(emailOrUsername);
            if (user == null) return false;

            var newHash = HashPassword(newPassword);
            user.PasswordHash = newHash;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
