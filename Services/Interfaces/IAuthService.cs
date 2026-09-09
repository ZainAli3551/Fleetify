using System.Threading.Tasks;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;

namespace Fleetify.Services.Interfaces
{
    public interface IAuthService
    {
        string HashPassword(string password);
        bool VerifyPassword(string password, string hashedPassword);
        string GenerateJwtToken(BaseUser user);
        Task<BaseUser?> AuthenticateAsync(string email, string password, string role);
        Task<bool> UserExistsAsync(string email);
        Task<string?> GetUserRoleByEmailAsync(string email);
        Task<User> RegisterCustomerAsync(RegisterViewModel model);
        Task<Driver> RegisterDriverAsync(RegisterViewModel model);
        Task<Admin> RegisterAdminAsync(RegisterViewModel model);
    }
}
