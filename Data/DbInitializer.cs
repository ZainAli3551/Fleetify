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

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'VerifiedWeight')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD VerifiedWeight FLOAT NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'WeightStatus')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD WeightStatus NVARCHAR(50) NULL DEFAULT 'PendingVerification';
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'DriverVerificationNotes')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD DriverVerificationNotes NVARCHAR(500) NULL;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'OriginType')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD OriginType NVARCHAR(50) NOT NULL DEFAULT 'DoorstepPickup';
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'DestinationType')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD DestinationType NVARCHAR(50) NOT NULL DEFAULT 'DoorstepDelivery';
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'OriginCity')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD OriginCity NVARCHAR(100) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'DestinationCity')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD DestinationCity NVARCHAR(100) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'HasOriginWarehouse')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD HasOriginWarehouse BIT NOT NULL DEFAULT 1;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'HasDestinationWarehouse')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD HasDestinationWarehouse BIT NOT NULL DEFAULT 1;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'IsPrivateTransport')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD IsPrivateTransport BIT NOT NULL DEFAULT 0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'PrivateTransportSurcharge')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD PrivateTransportSurcharge FLOAT NOT NULL DEFAULT 0.0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'DestinationWarehouseName')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD DestinationWarehouseName NVARCHAR(150) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'DestinationWarehouseAddress')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD DestinationWarehouseAddress NVARCHAR(250) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'WarehouseArrivalNotified')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD WarehouseArrivalNotified BIT NOT NULL DEFAULT 0;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Drivers') AND name = 'DriverType')
                    BEGIN
                        ALTER TABLE Drivers ADD DriverType NVARCHAR(30) NOT NULL DEFAULT 'Company';
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Drivers') AND name = 'BackupContactNotes')
                    BEGIN
                        ALTER TABLE Drivers ADD BackupContactNotes NVARCHAR(500) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Drivers') AND name = 'VehicleOwned')
                    BEGIN
                        ALTER TABLE Drivers ADD VehicleOwned NVARCHAR(100) NULL;
                    END

                    UPDATE Vehicles SET VehicleType = 'Pickup', CapacityKg = 800 WHERE VehicleType = 'Car';
                    UPDATE DeliveryRequests SET VehicleType = 'Pickup' WHERE VehicleType = 'Car';
                ");

                // Seed initial backup drivers if none exist
                var hasBackupDrivers = await context.Drivers.AnyAsync(d => d.DriverType == "Backup");
                if (!hasBackupDrivers)
                {
                    context.Drivers.AddRange(
                        new Models.Entities.Driver
                        {
                            FullName = "Babar Azam (Backup Partner)",
                            Email = "babar.backup@fleetify.com",
                            PasswordHash = authService.HashPassword("Password@123"),
                            PhoneNumber = "+923017778899",
                            LicenseNumber = "PK-LHR-88219",
                            Role = "Driver",
                            AvailabilityStatus = "Available",
                            DriverType = "Backup",
                            VehicleOwned = "Suzuki Ravi Pickup (LES-4912)",
                            BackupContactNotes = "On-call freelance partner. Available evenings, weekends, Lahore & Kasur dedicated routes."
                        },
                        new Models.Entities.Driver
                        {
                            FullName = "Kamran Akmal (Backup Partner)",
                            Email = "kamran.backup@fleetify.com",
                            PasswordHash = authService.HashPassword("Password@123"),
                            PhoneNumber = "+923218889900",
                            LicenseNumber = "PK-KHI-44102",
                            Role = "Driver",
                            AvailabilityStatus = "Available",
                            DriverType = "Backup",
                            VehicleOwned = "Hyundai Shehzore Van (KHI-7123)",
                            BackupContactNotes = "On-call 1.5 ton vehicle owner. Available for Karachi, Hub, and interior Sindh dispatches."
                        }
                    );
                    await context.SaveChangesAsync();
                }

                // Ensure default Admin exists
                var adminUser = await context.Admins.FirstOrDefaultAsync(a => a.Email == "admin@fleetify.com");
                if (adminUser == null)
                {
                    context.Admins.Add(new Models.Entities.Admin
                    {
                        FullName = "Fleet Operations Admin",
                        Email = "admin@fleetify.com",
                        PasswordHash = authService.HashPassword("admin123"),
                        PhoneNumber = "+923001234567",
                        Role = "Admin",
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                }

                // Ensure default Customer exists
                var custUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "customer@fleetify.com");
                if (custUser == null)
                {
                    context.Users.Add(new Models.Entities.User
                    {
                        FullName = "Demo Customer",
                        Email = "customer@fleetify.com",
                        PasswordHash = authService.HashPassword("customer123"),
                        PhoneNumber = "+923007654321",
                        Role = "Customer",
                        Address = "Main Boulevard, Gulberg III, Lahore",
                        AccountStatus = "Active",
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] Column check note: {ex.Message}");
            }
        }
    }
}
