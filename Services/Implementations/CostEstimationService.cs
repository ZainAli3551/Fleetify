using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Fleetify.Data;
using Fleetify.Models.Entities;
using Fleetify.Models.ViewModels;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class CostEstimationService : ICostEstimationService
    {
        private readonly FleetifyDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IMapRoutingService _mapRoutingService;

        public CostEstimationService(
            FleetifyDbContext context,
            IConfiguration configuration,
            IMapRoutingService mapRoutingService)
        {
            _context = context;
            _configuration = configuration;
            _mapRoutingService = mapRoutingService;
        }

        public static double CalculatePetrolSurcharge(double distanceKm)
        {
            return 0.0; // Petrol surcharge completely removed as requested
        }

        public static double GetDeliveryMultiplier(string? deliveryType)
        {
            if (string.IsNullOrWhiteSpace(deliveryType)) return 1.0;
            if (deliveryType.Equals("Fast", StringComparison.OrdinalIgnoreCase)) return 1.75;
            if (deliveryType.Equals("Express", StringComparison.OrdinalIgnoreCase)) return 2.5;
            return 1.0; // Normal
        }

        public async Task<CostEstimateResult> CalculateCostAsync(CostEstimateRequest request)
        {
            double ratePerKmKg = _configuration.GetValue<double>("AiSettings:PerKmPerKgRate", 1.50);

            // Fetch Real-world Driving Road Distance from Map Routing Service (Google Maps / OSRM)
            double distanceKm = request.DistanceKm;
            int durationMinutes = 30;
            string? polyline = null;
            string provider = "Local";

            if (!string.IsNullOrWhiteSpace(request.PickupLocation) && !string.IsNullOrWhiteSpace(request.DropoffLocation))
            {
                var routeResult = await _mapRoutingService.GetDrivingDistanceAsync(request.PickupLocation, request.DropoffLocation);
                if (routeResult.Success && routeResult.DistanceKm > 0)
                {
                    distanceKm = routeResult.DistanceKm;
                    durationMinutes = routeResult.DurationMinutes;
                    polyline = routeResult.EncodedPolyline;
                    provider = routeResult.Provider;
                }
                else
                {
                    distanceKm = EstimateDistance(request.PickupLocation, request.DropoffLocation, request.DistanceKm);
                }
            }

            double weight = request.ParcelWeight > 0 ? request.ParcelWeight : 2.0;

            // Floor numbers down to previous whole number (e.g. 2.535 -> 2, 100.7 -> 100)
            double flooredWeight = Math.Floor(weight);
            if (flooredWeight < 1.0) flooredWeight = 1.0;

            double flooredDistance = Math.Floor(distanceKm);
            if (flooredDistance < 1.0) flooredDistance = 1.0;

            string deliveryType = string.IsNullOrWhiteSpace(request.RouteType) ? "Normal" : request.RouteType;
            if (deliveryType.Equals("Standard", StringComparison.OrdinalIgnoreCase)) deliveryType = "Normal";
            double deliveryMultiplier = GetDeliveryMultiplier(deliveryType);

            // Formula: Math.Floor(1.5 * Distance * Weight * DeliveryMultiplier) - Petrol Surcharge completely removed
            double baseCost = Math.Floor(flooredDistance * flooredWeight * ratePerKmKg);
            double total = Math.Floor(baseCost * deliveryMultiplier);

            string deliveryDays = "1-2 business days";
            if (deliveryType.Equals("Express", StringComparison.OrdinalIgnoreCase))
            {
                deliveryDays = "Same-day express delivery";
            }
            else if (deliveryType.Equals("Fast", StringComparison.OrdinalIgnoreCase))
            {
                deliveryDays = "Next-day fast delivery";
            }
            else if (flooredDistance > 400)
            {
                deliveryDays = "3-5 business days";
            }
            else if (flooredDistance > 100)
            {
                deliveryDays = "2-3 business days";
            }

            return new CostEstimateResult
            {
                DistanceKm = flooredDistance,
                ParcelWeight = flooredWeight,
                RatePerKmKg = ratePerKmKg,
                BaseCost = baseCost,
                PetrolCharge = 0.0,
                DeliveryType = deliveryType,
                DeliveryMultiplier = deliveryMultiplier,
                Currency = "Rs.",
                EstimatedTotal = total,
                EstimatedDeliveryDays = deliveryDays,
                DurationMinutes = durationMinutes,
                RoutePolyline = polyline,
                Provider = provider
            };
        }

        public CostEstimateResult CalculateCost(CostEstimateRequest request)
        {
            try
            {
                return CalculateCostAsync(request).GetAwaiter().GetResult();
            }
            catch
            {
                double ratePerKmKg = _configuration.GetValue<double>("AiSettings:PerKmPerKgRate", 1.50);
                double distanceKm = EstimateDistance(request.PickupLocation, request.DropoffLocation, request.DistanceKm);
                double weight = request.ParcelWeight > 0 ? request.ParcelWeight : 2.0;
                double flooredWeight = Math.Floor(weight);
                if (flooredWeight < 1.0) flooredWeight = 1.0;

                double flooredDistance = Math.Floor(distanceKm);
                if (flooredDistance < 1.0) flooredDistance = 1.0;

                string deliveryType = string.IsNullOrWhiteSpace(request.RouteType) ? "Normal" : request.RouteType;
                if (deliveryType.Equals("Standard", StringComparison.OrdinalIgnoreCase)) deliveryType = "Normal";
                double deliveryMultiplier = GetDeliveryMultiplier(deliveryType);

                double baseCost = Math.Floor(flooredDistance * flooredWeight * ratePerKmKg);
                double total = Math.Floor(baseCost * deliveryMultiplier);

                return new CostEstimateResult
                {
                    DistanceKm = flooredDistance,
                    ParcelWeight = flooredWeight,
                    RatePerKmKg = ratePerKmKg,
                    BaseCost = baseCost,
                    PetrolCharge = 0.0,
                    DeliveryType = deliveryType,
                    DeliveryMultiplier = deliveryMultiplier,
                    Currency = "Rs.",
                    EstimatedTotal = total
                };
            }
        }

        private double EstimateDistance(string? pickup, string? dropoff, double fallbackDistance)
        {
            if (string.IsNullOrWhiteSpace(pickup) || string.IsNullOrWhiteSpace(dropoff))
            {
                return fallbackDistance > 0 ? fallbackDistance : 15.0;
            }

            var p = pickup.ToLower();
            var d = dropoff.ToLower();

            // Known intercity route matrix
            if ((p.Contains("lahore") && d.Contains("karachi")) || (p.Contains("karachi") && d.Contains("lahore"))) return 1215.0;
            if ((p.Contains("lahore") && d.Contains("islamabad")) || (p.Contains("islamabad") && d.Contains("lahore"))) return 375.0;
            if ((p.Contains("gujranwala") && d.Contains("lahore")) || (p.Contains("lahore") && d.Contains("gujranwala"))) return 72.0;
            if ((p.Contains("gujranwala") && d.Contains("islamabad")) || (p.Contains("islamabad") && d.Contains("gujranwala"))) return 215.0;
            if ((p.Contains("gujranwala") && d.Contains("sialkot")) || (p.Contains("sialkot") && d.Contains("gujranwala"))) return 52.0;
            if ((p.Contains("new york") && d.Contains("boston")) || (p.Contains("boston") && d.Contains("new york"))) return 346.0;
            if ((p.Contains("chicago") && d.Contains("miami")) || (p.Contains("miami") && d.Contains("chicago"))) return 1910.0;

            // Check if both are in same city
            var cities = new[] { "gujranwala", "lahore", "karachi", "islamabad", "faisalabad", "sialkot", "rawalpindi", "multan", "peshawar", "new york", "boston", "chicago" };
            foreach (var city in cities)
            {
                if (p.Contains(city) && d.Contains(city))
                {
                    // Intra-city route: 8 to 22 km based on address string
                    int hash = Math.Abs((p + d).GetHashCode()) % 15;
                    return 8.0 + hash;
                }
            }

            // General location estimation: deterministic realistic distance based on input text
            int seed = Math.Abs((p + "|" + d).GetHashCode());
            double estimatedKm = 12.0 + (seed % 45); // between 12 km and 57 km
            return Math.Round(estimatedKm, 1);
        }

        public async Task<CostEstimate> SaveCostEstimateAsync(int deliveryRequestId, CostEstimateRequest request, CostEstimateResult result)
        {
            var estimate = new CostEstimate
            {
                RequestID = deliveryRequestId,
                DistanceKm = request.DistanceKm,
                ParcelWeight = request.ParcelWeight,
                VehicleType = request.VehicleType,
                RouteType = request.RouteType,
                BaseFare = result.BaseRate,
                DistanceCharge = result.DistanceCharge,
                WeightCharge = result.WeightCharge,
                TypeSurcharge = result.TypeSurcharge,
                EstimatedAmount = result.EstimatedTotal,
                GeneratedAt = DateTime.UtcNow
            };

            _context.CostEstimates.Add(estimate);
            await _context.SaveChangesAsync();
            return estimate;
        }
    }
}
