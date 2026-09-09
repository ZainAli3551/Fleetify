namespace Fleetify.Models.Common
{
    public class RouteDistanceResult
    {
        public bool Success { get; set; }
        public double DistanceKm { get; set; }
        public int DurationMinutes { get; set; }
        public string Origin { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public double? OriginLat { get; set; }
        public double? OriginLng { get; set; }
        public double? DestinationLat { get; set; }
        public double? DestinationLng { get; set; }
        public string? EncodedPolyline { get; set; }
        public string Provider { get; set; } = "Default";
        public string? ErrorMessage { get; set; }
    }
}
