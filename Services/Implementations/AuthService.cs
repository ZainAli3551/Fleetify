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
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch
            {
                return false;
            }
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

        public async Task<BaseUser?> AuthenticateAsync(string email, string password, string role)
        {
            email = email.Trim().ToLower();

            // STRICT ROLE ENFORCEMENT: ONLY authenticate against the selected role's table
            BaseUser? user = role.ToLower() switch
            {
                "admin" => await _context.Admins.FirstOrDefaultAsync(a => a.Email.ToLower() == email),
                "driver" => await _context.Drivers.FirstOrDefaultAsync(d => d.Email.ToLower() == email),
                "customer" => await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email),
                _ => null
            };

            // Only return user if password matches and user belongs to the requested role
            if (user != null && VerifyPassword(password, user.PasswordHash))
            {
                return user;
            }

            return null;
        }

        public async Task<string?> GetUserRoleByEmailAsync(string email)
        {
            email = email.Trim().ToLower();
            if (await _context.Admins.AnyAsync(a => a.Email.ToLower() == email)) return "Admin";
            if (await _context.Drivers.AnyAsync(d => d.Email.ToLower() == email)) return "Driver";
            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email)) return "Customer";
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
                Address = model.Address ?? "123 Main Street",
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
    }
}
