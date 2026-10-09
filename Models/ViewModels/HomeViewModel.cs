namespace Fleetify.Models.ViewModels
{
    public class HomeViewModel
    {
        public int ActiveFleets { get; set; } = 100;
        public double DeliverySuccessRate { get; set; } = 99.9;
        public int TotalShipments { get; set; } = 1500;
        public int CitiesCovered { get; set; } = 10;
    }
}
