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
                    UPDATE DeliveryRequests SET PackageName = ISNULL(ParcelDescription, 'General Item') WHERE PackageName IS NULL;
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

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Vehicles') AND name = 'InitialMileage')
                    BEGIN
                        ALTER TABLE Vehicles ADD InitialMileage FLOAT NOT NULL DEFAULT 0.0;
                        EXEC('UPDATE Vehicles SET InitialMileage = CurrentMileage WHERE InitialMileage = 0.0;');
                    END
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Vehicles') AND name = 'CompletedRouteMileage')
                    BEGIN
                        ALTER TABLE Vehicles ADD CompletedRouteMileage FLOAT NOT NULL DEFAULT 0.0;
                    END

                    UPDATE Vehicles SET VehicleType = 'Pickup', CapacityKg = 800 WHERE VehicleType = 'Car';
                    UPDATE DeliveryRequests SET VehicleType = 'Pickup' WHERE VehicleType = 'Car';
                ");

                // Ensure default Admin exists for administrative access
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
                var defaultCustomer = await context.Users.FirstOrDefaultAsync(u => u.Email == "customer@fleetify.com");
                if (defaultCustomer == null)
                {
                    context.Users.Add(new Models.Entities.User
                    {
                        FullName = "Demo Customer",
                        Email = "customer@fleetify.com",
                        PasswordHash = authService.HashPassword("customer123"),
                        PhoneNumber = "+923009876543",
                        Role = "Customer",
                        Address = "House 14, Street 5, Model Town, Lahore",
                        AccountStatus = "Active",
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                }

                // Ensure default System Driver exists
                var defaultDriver = await context.Drivers.FirstOrDefaultAsync(d => d.Email == "driver@fleetify.com");
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
                    await context.SaveChangesAsync();
                }

                // Ensure default Vehicle exists
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
                Console.WriteLine($"[DbInitializer] Column check note: {ex.Message}");
            }
        }
    }
}
