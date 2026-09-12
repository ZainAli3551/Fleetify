using System.Collections.Generic;
using Fleetify.Models.Entities;

namespace Fleetify.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int ActiveDrivers { get; set; }
        public int TotalDeliveries { get; set; }
        public double Revenue { get; set; }
        public int CompletedToday { get; set; }
        public int InTransitCount { get; set; }
        public int PendingCount { get; set; }

        public List<DeliveryRequest> RecentDeliveries { get; set; } = new();
        public List<MaintenanceAlert> ActiveMaintenanceAlerts { get; set; } = new();
        public List<Driver> Drivers { get; set; } = new();
        public List<Vehicle> Vehicles { get; set; } = new();

        // Chart data points
        public List<string> MonthlyLabels { get; set; } = new() { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul" };
        public List<double> RevenueTrend { get; set; } = new() { 2500, 4800, 7200, 6800, 9400, 11200, 14500 };
        public List<string> WeekDays { get; set; } = new() { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        public List<int> WeeklyDeliveries { get; set; } = new() { 42, 55, 63, 50, 78, 35, 22 };
    }

    public class DriverDashboardViewModel
    {
        public int DriverId { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string LicenseNumber { get; set; } = string.Empty;
        public string AvailabilityStatus { get; set; } = "Available";

        public int TodaysDeliveries { get; set; }
        public int CompletedDeliveries { get; set; }
        public double DistanceTodayKm { get; set; }
        public double EarningsToday { get; set; }

        public List<Assignment> AvailableAssignments { get; set; } = new();
        public List<Assignment> ActiveAssignments { get; set; } = new();
        public List<Assignment> CompletedAssignments { get; set; } = new();
    }

    public class CustomerDashboardViewModel
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;

        public int TotalDeliveries { get; set; }
        public int InTransitCount { get; set; }
        public int DeliveredCount { get; set; }
        public double TotalSpent { get; set; }

        public DeliveryRequestInputModel NewDelivery { get; set; } = new();
        public List<DeliveryRequest> MyDeliveries { get; set; } = new();
    }

    public class DeliveryRequestInputModel
    {
        public string PickupLocation { get; set; } = string.Empty;
        public string DropoffLocation { get; set; } = string.Empty;
        public double ParcelWeight { get; set; } = 2.0;
        public double Height { get; set; } = 1.0;
        public double Width { get; set; } = 1.0;
        public string ParcelDescription { get; set; } = string.Empty;
        public string VehicleType { get; set; } = "Truck"; // Bike, Pickup, Van, Truck
        public string RouteType { get; set; } = "Normal"; // Normal, Fast, Express
        public double EstimatedDistanceKm { get; set; } = 15.0;
        public DateTime PickupDate { get; set; } = DateTime.Today;
    }

    public class AdminSupportDashboardViewModel
    {
        public int TotalTickets { get; set; }
        public int BotHandledCount { get; set; }
        public int NeedsAdminCount { get; set; }
        public int ResolvedCount { get; set; }

        public string CurrentFilter { get; set; } = "All";
        public List<SupportConversation> Conversations { get; set; } = new();
        public SupportConversation? ActiveConversation { get; set; }
    }
}

