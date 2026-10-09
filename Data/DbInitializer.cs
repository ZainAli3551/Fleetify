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
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'Length')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD Length FLOAT NOT NULL DEFAULT 30.0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'Height')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD Height FLOAT NOT NULL DEFAULT 15.0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'Width')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD Width FLOAT NOT NULL DEFAULT 20.0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'PackageName')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD PackageName NVARCHAR(200) NULL;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'Quantity')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD Quantity INT NOT NULL DEFAULT 1;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'TotalCubicFeet')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD TotalCubicFeet FLOAT NOT NULL DEFAULT 0.0;
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'SingleItemWeight')
                    BEGIN
                        ALTER TABLE DeliveryRequests ADD SingleItemWeight FLOAT NOT NULL DEFAULT 2.0;
                    END
                    
                    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryRequests') AND name = 'ParcelDescription')
                    BEGIN
                        EXEC('UPDATE DeliveryRequests SET PackageName = ISNULL(ParcelDescription, ''General Item'') WHERE PackageName IS NULL;');
                    END
                    ELSE
                    BEGIN
                        EXEC('UPDATE DeliveryRequests SET PackageName = ''General Item'' WHERE PackageName IS NULL;');
                    END

                    EXEC('UPDATE DeliveryRequests SET SingleItemWeight = CASE WHEN Quantity > 0 THEN ROUND(ParcelWeight / Quantity, 2) ELSE ParcelWeight END WHERE SingleItemWeight IS NULL OR SingleItemWeight = 0;');

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] Migration check note: {ex.Message}");
            }

            try
            {
                // 1. Ensure Admin exists and has valid password
                var adminUser = await context.Admins.FirstOrDefaultAsync(a => a.Email.ToLower() == "admin@fleetify.com");
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
                }
                else
                {
                    // Ensure password matches admin123
                    adminUser.PasswordHash = authService.HashPassword("admin123");
                }
                await context.SaveChangesAsync();

                // 2. Ensure default System Driver exists
                var defaultDriver = await context.Drivers.FirstOrDefaultAsync(d => d.Email.ToLower() == "driver@fleetify.com");
                if (defaultDriver == null)
                {
                    context.Drivers.Add(new Models.Entities.Driver
                    {
                        FullName = "Fleet System Driver",
                        Email = "driver@fleetify.com",
                        PasswordHash = authService.HashPassword("driver123"),
                        PhoneNumber = "+923007654321",
                        Role = "Driver",
                        LicenseNumber = "LIC-FLT-2026",
                        AvailabilityStatus = "Available",
                        DriverType = "Company",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    defaultDriver.PasswordHash = authService.HashPassword("driver123");
                }

                // Update any other existing drivers to driver123 so backup drivers work smoothly
                var existingDrivers = await context.Drivers.ToListAsync();
                foreach (var d in existingDrivers)
                {
                    d.PasswordHash = authService.HashPassword("driver123");
                }
                await context.SaveChangesAsync();

                // 3. Ensure default Customer exists
                var defaultCustomer = await context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == "customer@fleetify.com");
                if (defaultCustomer == null)
                {
                    context.Users.Add(new Models.Entities.User
                    {
                        FullName = "Demo Customer",
                        Email = "customer@fleetify.com",
                        PasswordHash = authService.HashPassword("customer123"),
                        PhoneNumber = "03001234567",
                        Role = "Customer",
                        AccountStatus = "Active",
                        Address = "Lahore, Pakistan",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    defaultCustomer.PasswordHash = authService.HashPassword("customer123");
                }
                await context.SaveChangesAsync();

                // 4. Ensure default Vehicle exists
                var defaultVehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.VehicleNumber == "GAM-3551");
                if (defaultVehicle == null)
                {
                    context.Vehicles.Add(new Models.Entities.Vehicle
                    {
                        VehicleNumber = "GAM-3551",
                        VehicleType = "Truck",
                        Model = "Toyota Hiace 2024",
                        CapacityKg = 1500,
                        AvailabilityStatus = "Available",
                        ConditionStatus = "Good",
                        CurrentMileage = 3500,
                        LastServiceMileage = 3000
                    });
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DbInitializer] User/Vehicle seeding note: {ex.Message}");
            }
        }
    }
}
