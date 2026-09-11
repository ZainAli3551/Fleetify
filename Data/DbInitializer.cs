using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Fleetify.Services.Interfaces;

namespace Fleetify.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(FleetifyDbContext context, IAuthService authService)
        {
            // Ensure the database and all its tables are created cleanly without injecting any dummy/demo data
            await context.Database.EnsureCreatedAsync();

            try
            {
                // Ensure Height and Width columns exist in SQL Server if the table was created before
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'Height')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD Height FLOAT NOT NULL DEFAULT 1.0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'Width')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD Width FLOAT NOT NULL DEFAULT 1.0;
                    END
                ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] Column check note: {ex.Message}");
            }
        }
    }
}
