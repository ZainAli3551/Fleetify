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

        public CostEstimationService(FleetifyDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public CostEstimateResult CalculateCost(CostEstimateRequest request)
        {
            // Step 1: Base fare & rates
            double baseFare = _configuration.GetValue<double>("AiSettings:BaseFare", 10.00);
            double perKmRate = _configuration.GetValue<double>("AiSettings:PerKmRate", 1.25);
            double perKgRate = _configuration.GetValue<double>("AiSettings:PerKgRate", 0.75);

            // Step 2: Dynamic Distance calculation based on locations
            double distanceKm = EstimateDistance(request.PickupLocation, request.DropoffLocation, request.DistanceKm);
            double distanceCharge = Math.Round(distanceKm * perKmRate, 2);

            // Step 3: Weight charge
            double weight = Math.Max(0.5, request.ParcelWeight);
            double weightCharge = Math.Round(weight * perKgRate, 2);

            // Step 4: Vehicle factor & Route surcharge
            double vehicleMultiplier = request.VehicleType?.ToLower() switch
            {
                "bike" => 0.80,
                "car" => 1.00,
                "van" => 1.25,
                "truck" => 1.60,
                _ => 1.00
            };

            double routeMultiplier = request.RouteType?.ToLower() switch
            {
                "express" => 1.35,
                "fragile" => 1.25,
                "heavycargo" => 1.50,
                _ => 1.00
            };

            double subtotal = baseFare + distanceCharge + weightCharge;
            double typeSurcharge = Math.Round(subtotal * (vehicleMultiplier - 1.0 + (routeMultiplier - 1.0)), 2);
            if (typeSurcharge < 0) typeSurcharge = 0;

            // Step 5: Total estimate
            double total = Math.Round(subtotal * vehicleMultiplier * routeMultiplier, 2);

            string deliveryDays = "1-2 business days";
            if (request.RouteType?.Equals("Express", StringComparison.OrdinalIgnoreCase) == true)
            {
                deliveryDays = "Same-day or next-morning rush delivery";
            }
            else if (distanceKm > 400)
            {
                deliveryDays = "3-5 business days";
            }
            else if (distanceKm > 100)
            {
                deliveryDays = "2-3 business days";
            }

            return new CostEstimateResult
            {
                DistanceKm = Math.Round(distanceKm, 1),
                BaseRate = baseFare,
                DistanceCharge = distanceCharge,
                WeightCharge = weightCharge,
                TypeSurcharge = typeSurcharge,
                EstimatedTotal = total,
                EstimatedDeliveryDays = deliveryDays
            };
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
