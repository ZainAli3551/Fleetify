using System.Threading.Tasks;
using Fleetify.Services.Interfaces;

namespace Fleetify.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(FleetifyDbContext context, IAuthService authService)
        {
            // Ensure the database and all its tables are created cleanly without injecting any dummy/demo data
            await context.Database.EnsureCreatedAsync();
        }
    }
}
