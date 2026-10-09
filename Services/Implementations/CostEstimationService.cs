using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

        public static double CalculateDistanceCharge(double distanceKm)
        {
            if (distanceKm < 50.0) return 1500.0;
            if (distanceKm < 150.0) return 2500.0;
            if (distanceKm < 250.0) return 4000.0;
            if (distanceKm < 500.0) return 7000.0;
            if (distanceKm < 1000.0) return 12000.0;
            if (distanceKm < 1500.0) return 18000.0;
            return 18000.0; // 1500km+
        }

        public static double CalculatePetrolSurcharge(double distanceKm)
        {
            return CalculateDistanceCharge(distanceKm);
        }

        public static double CalculateDoorstepFuelSurcharge(double totalCubicFeet)
        {
            // Up to 300 cubic feet: Rs. 1,000 per doorstep trip
            // Over 300 to 1,200 cubic feet: Rs. 2,000 per doorstep trip
            // Over 1,200 to 3,500 cubic feet: Rs. 4,000 per doorstep trip
            // Over 3,500 cubic feet: Rs. 5,000 per doorstep trip
            if (totalCubicFeet <= 300.0) return 1000.0;
            if (totalCubicFeet <= 1200.0) return 2000.0;
            if (totalCubicFeet <= 3500.0) return 4000.0;
            return 5000.0;
        }

        public static double CalculateWeightCharge(double weightKg)
        {
            if (weightKg <= 0) return 0.0;
            // IF(B2<1000, B2*2, ((B2-MOD(B2,1000))/1000*200) + (MOD(B2,1000)*2))
            if (weightKg < 1000.0)
            {
                return weightKg * 2.0;
            }
            double modVal = weightKg % 1000.0;
            double fullTons = Math.Floor((weightKg - modVal) / 1000.0);
            return (fullTons * 200.0) + (modVal * 2.0);
        }

        public static (double length, double width, double height, double cft) ParseDimensions(string? dimStr, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(dimStr)) return (0.0, 0.0, 0.0, 0.0);
            var matches = System.Text.RegularExpressions.Regex.Matches(dimStr, @"[0-9]+(\.[0-9]+)?");
            var nums = new System.Collections.Generic.List<double>();
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (double.TryParse(m.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0)
                {
                    nums.Add(n);
                }
            }
            int q = quantity > 0 ? quantity : 1;
            if (nums.Count >= 3)
            {
                double l = nums[0];
                double w = nums[1];
                double h = nums[2];
                double cft = Math.Round(l * w * h * q, 2);
                return (l, w, h, cft);
            }
            if (nums.Count == 2)
            {
                double l = nums[0];
                double w = nums[1];
                double cft = Math.Round(l * w * q, 2);
                return (l, w, 1.0, cft);
            }
            if (nums.Count == 1)
            {
                return (nums[0], 1.0, 1.0, Math.Round(nums[0] * q, 2));
            }
            return (0.0, 0.0, 0.0, 0.0);
        }

        public static double CalculateDoorstepWeightSurcharge(double totalWeightKg)
        {
            if (totalWeightKg <= 0) return 0.0;
            // If weight is less than 1 ton (1000 kg), charge Rs. 1 per kg.
            if (totalWeightKg < 1000.0)
            {
                return totalWeightKg * 1.0;
            }
            // If 1 ton or more (>= 1000 kg), charge Rs. 200 per ton (1000 kg).
            return (totalWeightKg / 1000.0) * 200.0;
        }

        public static double GetDeliveryMultiplier(string? deliveryType)
        {
            if (string.IsNullOrWhiteSpace(deliveryType)) return 1.0;
            var d = deliveryType.Trim().ToLowerInvariant();
            if (d == "fast") return 1.75;
            if (d == "express" || d == "urgent") return 2.5;
            return 1.0; // Normal
        }

        public static double GetVolumeMultiplier(double totalCubicFeet)
        {
            // IFS(D2<1000, 1, D2<2000, 1.25, D2<3000, 1.40, D2>=3000, 1.50)
            if (totalCubicFeet < 1000.0) return 1.0;
            if (totalCubicFeet < 2000.0) return 1.25;
            if (totalCubicFeet < 3000.0) return 1.40;
            return 1.50;
        }

        public static double GetDoorstepHubDistance(string? city, string? address)
        {
            if (string.IsNullOrWhiteSpace(city)) city = WarehouseNetworkService.DetectCity(address ?? "");
            return (city?.ToLowerInvariant()) switch
            {
                "gujranwala" => 9.2,
                "lahore" => 14.5,
                "islamabad" => 8.4,
                "rawalpindi" => 12.6,
                "faisalabad" => 10.3,
                "multan" => 11.2,
                "peshawar" => 13.1,
                "sialkot" => 16.4,
                "karachi" => 12.5,
                "quetta" => 7.3,
                _ => 10.0
            };
        }

        public async Task<CostEstimateResult> CalculateCostAsync(CostEstimateRequest request)
        {
            double ratePerKmKg = _configuration.GetValue<double>("AiSettings:PerKmPerKgRate", 1.50);

            // Resolve Origin and Destination Warehouses
            var originWarehouse = WarehouseNetworkService.GetWarehouse(request.PickupLocation);
            var destWarehouse = WarehouseNetworkService.GetWarehouse(request.DropoffLocation);

            string originWarehouseAddress = !string.IsNullOrWhiteSpace(request.OriginWarehouse)
                ? request.OriginWarehouse
                : (originWarehouse?.Address ?? request.PickupLocation ?? string.Empty);

            string destWarehouseAddress = !string.IsNullOrWhiteSpace(request.DestinationWarehouse)
                ? request.DestinationWarehouse
                : (destWarehouse?.Address ?? request.DropoffLocation ?? string.Empty);

            string mainOrigin = !string.IsNullOrWhiteSpace(originWarehouseAddress) ? originWarehouseAddress : (request.PickupLocation ?? string.Empty);
            string mainDest = !string.IsNullOrWhiteSpace(destWarehouseAddress) ? destWarehouseAddress : (request.DropoffLocation ?? string.Empty);

            // Fetch Real-world Driving Road Distance for Main Hub-to-Hub Route
            double distanceKm = request.DistanceKm;
            int durationMinutes = 30;
            string? polyline = null;
            string provider = "Local";

            double? originLat = null;
            double? originLng = null;
            double? destinationLat = null;
            double? destinationLng = null;

            if (!string.IsNullOrWhiteSpace(mainOrigin) && !string.IsNullOrWhiteSpace(mainDest))
            {
                var routeResult = await _mapRoutingService.GetDrivingDistanceAsync(mainOrigin, mainDest);
                if (routeResult.Success && routeResult.DistanceKm > 0)
                {
                    distanceKm = routeResult.DistanceKm;
                    durationMinutes = routeResult.DurationMinutes;
                    polyline = routeResult.EncodedPolyline;
                    provider = routeResult.Provider;
                    originLat = routeResult.OriginLat;
                    originLng = routeResult.OriginLng;
                    destinationLat = routeResult.DestinationLat;
                    destinationLng = routeResult.DestinationLng;
                }
                else
                {
                    distanceKm = EstimateDistance(mainOrigin, mainDest, request.DistanceKm);
                    var pCoord = MapRoutingService.ResolveCityCoordinates(mainOrigin);
                    var dCoord = MapRoutingService.ResolveCityCoordinates(mainDest);
                    if (pCoord.HasValue) { originLat = pCoord.Value.Lat; originLng = pCoord.Value.Lng; }
                    if (dCoord.HasValue) { destinationLat = dCoord.Value.Lat; destinationLng = dCoord.Value.Lng; }
                    if (originLat.HasValue && destinationLat.HasValue)
                    {
                        polyline = MapRoutingService.EncodePolyline(new[] { (originLat.Value, originLng!.Value), (destinationLat.Value, destinationLng!.Value) });
                    }
                }
            }

            double weight = request.ParcelWeight >= 0 ? request.ParcelWeight : 0.0;
            if (distanceKm < 1.0) distanceKm = 1.0;

            string deliveryType = string.IsNullOrWhiteSpace(request.RouteType) ? "Normal" : request.RouteType;
            if (deliveryType.Equals("Standard", StringComparison.OrdinalIgnoreCase)) deliveryType = "Normal";
            double deliveryMultiplier = GetDeliveryMultiplier(deliveryType);

            // Warehouse Network & Special Delivery Check
            var originCity = WarehouseNetworkService.DetectCity(mainOrigin);
            var destCity = WarehouseNetworkService.DetectCity(mainDest);
            bool hasOriginWarehouse = request.OriginType == "WarehouseDropoff" || WarehouseNetworkService.HasWarehouse(mainOrigin);
            bool hasDestWarehouse = string.IsNullOrWhiteSpace(mainDest) || WarehouseNetworkService.HasWarehouse(mainDest);
            bool isSpecialDelivery = request.IsPrivateTransport || (!string.IsNullOrWhiteSpace(mainDest) && (WarehouseNetworkService.IsSpecialDeliveryCity(mainDest) || !hasDestWarehouse));

            bool isPickupDoorstep = request.OriginType?.Equals("DoorstepPickup", StringComparison.OrdinalIgnoreCase) == true;
            bool isDeliveryDoorstep = request.DestinationType?.Equals("DoorstepDelivery", StringComparison.OrdinalIgnoreCase) == true;

            // Accurate Leg Distances: Between Warehouse Hub and Selected Doorstep Location
            double pickupLegDistanceKm = isPickupDoorstep ? GetDoorstepHubDistance(originCity, request.PickupLocation) : 0.0;
            double deliveryLegDistanceKm = isDeliveryDoorstep ? GetDoorstepHubDistance(destCity, request.DropoffLocation) : 0.0;
            double flooredDistance = Math.Floor(distanceKm);
            double totalDrivingDistanceKm = Math.Round(flooredDistance + pickupLegDistanceKm + deliveryLegDistanceKm, 1);

            double totalCft = request.TotalCubicFeet;
            if (totalCft <= 0 && request.Length > 0 && request.Width > 0 && request.Height > 0)
            {
                totalCft = Math.Round(request.Length * request.Width * request.Height * (request.Quantity > 0 ? request.Quantity : 1), 2);
            }
            else if (totalCft <= 0 && !string.IsNullOrWhiteSpace(request.Dimensions))
            {
                var dims = ParseDimensions(request.Dimensions, request.Quantity);
                totalCft = dims.cft;
            }
            double volumeMultiplier = GetVolumeMultiplier(totalCft);

            // Exact Excel Rate Calculation Formula:
            // =((IF(B2<1000, B2*2, ((B2-MOD(B2,1000))/1000*200) + (MOD(B2,1000)*2)))+IFS(A2<50,1500,A2<150,2500,A2<250,4000,A2<500,7000,A2<1000,12000,A2<1500,18000))*SWITCH(C2,"Normal",1,"Fast",1.75,"Express",2.5,1)*IFS(D2<1000,1,D2<2000,1.25,D2<3000,1.40,D2>=3000,1.50)
            double weightCharge = CalculateWeightCharge(weight);
            double distanceCharge = CalculateDistanceCharge(totalDrivingDistanceKm > 0 ? totalDrivingDistanceKm : flooredDistance);
            double subtotal = (weightCharge + distanceCharge) * deliveryMultiplier * volumeMultiplier;
            double typeSurcharge = subtotal - (weightCharge + distanceCharge);
            double specialDeliverySurcharge = 0.0;
            double total = Math.Floor(subtotal);

            int doorstepLegCount = (isPickupDoorstep ? 1 : 0) + (isDeliveryDoorstep ? 1 : 0);
            double doorstepFuelSurcharge = CalculateDoorstepFuelSurcharge(totalCft);
            double doorstepWeightSurcharge = CalculateDoorstepWeightSurcharge(weight);
            double doorstepSurchargePerLeg = doorstepFuelSurcharge + doorstepWeightSurcharge;
            double pickupDoorstepSurcharge = isPickupDoorstep ? doorstepSurchargePerLeg : 0.0;
            double deliveryDoorstepSurcharge = isDeliveryDoorstep ? doorstepSurchargePerLeg : 0.0;
            double totalDoorstepSurcharge = pickupDoorstepSurcharge + deliveryDoorstepSurcharge;

            string deliveryDays = "5 - 6 Days";
            if (deliveryType.Equals("Fast", StringComparison.OrdinalIgnoreCase))
            {
                deliveryDays = "3 - 4 Days";
            }
            else if (deliveryType.Equals("Express", StringComparison.OrdinalIgnoreCase) || deliveryType.Equals("Urgent", StringComparison.OrdinalIgnoreCase))
            {
                deliveryDays = "1 - 2 Days";
            }

            return new CostEstimateResult
            {
                DistanceKm = flooredDistance,
                MainRouteDistanceKm = flooredDistance,
                PickupLegDistanceKm = pickupLegDistanceKm,
                DeliveryLegDistanceKm = deliveryLegDistanceKm,
                TotalDrivingDistanceKm = totalDrivingDistanceKm,
                ParcelWeight = weight,
                SingleItemWeight = request.SingleItemWeight > 0 ? request.SingleItemWeight : Math.Round(weight / (request.Quantity > 0 ? request.Quantity : 1), 2),
                Quantity = request.Quantity > 0 ? request.Quantity : 1,
                TotalCubicFeet = totalCft,
                VolumeMultiplier = volumeMultiplier,
                RatePerKmKg = 2.0,
                BaseCost = weightCharge,
                BaseRate = 0.00,
                WeightCharge = weightCharge,
                DistanceCharge = distanceCharge,
                PetrolCharge = distanceCharge,
                TypeSurcharge = typeSurcharge,
                DeliveryType = deliveryType,
                DeliveryMultiplier = deliveryMultiplier,
                IsPrivateTransport = isSpecialDelivery,
                PrivateTransportSurcharge = specialDeliverySurcharge,
                IsSpecialDelivery = isSpecialDelivery,
                SpecialDeliverySurcharge = specialDeliverySurcharge,

                // Doorstep Surcharges
                DoorstepLegCount = doorstepLegCount,
                DoorstepFuelSurchargePerLeg = doorstepFuelSurcharge,
                DoorstepWeightSurchargePerLeg = doorstepWeightSurcharge,
                DoorstepSurchargePerLeg = doorstepSurchargePerLeg,
                PickupDoorstepSurcharge = pickupDoorstepSurcharge,
                DeliveryDoorstepSurcharge = deliveryDoorstepSurcharge,
                TotalDoorstepSurcharge = totalDoorstepSurcharge,

                OriginCity = originCity,
                DestinationCity = destCity,
                HasOriginWarehouse = hasOriginWarehouse,
                HasDestinationWarehouse = hasDestWarehouse,
                OriginWarehouseName = originWarehouse?.HubName,
                DestinationWarehouseName = destWarehouse?.HubName,
                Currency = "Rs.",
                EstimatedTotal = total,
                EstimatedDeliveryDays = deliveryDays,
                DurationMinutes = durationMinutes,
                RoutePolyline = polyline,
                OriginLat = originLat,
                OriginLng = originLng,
                DestinationLat = destinationLat,
                DestinationLng = destinationLng,
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
                var originWarehouse = WarehouseNetworkService.GetWarehouse(request.PickupLocation);
                var destWarehouse = WarehouseNetworkService.GetWarehouse(request.DropoffLocation);

                string originWarehouseAddress = !string.IsNullOrWhiteSpace(request.OriginWarehouse)
                    ? request.OriginWarehouse
                    : (originWarehouse?.Address ?? request.PickupLocation ?? string.Empty);

                string destWarehouseAddress = !string.IsNullOrWhiteSpace(request.DestinationWarehouse)
                    ? request.DestinationWarehouse
                    : (destWarehouse?.Address ?? request.DropoffLocation ?? string.Empty);

                string mainOrigin = !string.IsNullOrWhiteSpace(originWarehouseAddress) ? originWarehouseAddress : (request.PickupLocation ?? string.Empty);
                string mainDest = !string.IsNullOrWhiteSpace(destWarehouseAddress) ? destWarehouseAddress : (request.DropoffLocation ?? string.Empty);

                double distanceKm = EstimateDistance(mainOrigin, mainDest, request.DistanceKm);
                double weight = request.ParcelWeight >= 0 ? request.ParcelWeight : 0.0;
                if (distanceKm < 1.0) distanceKm = 1.0;

                string deliveryType = string.IsNullOrWhiteSpace(request.RouteType) ? "Normal" : request.RouteType;
                if (deliveryType.Equals("Standard", StringComparison.OrdinalIgnoreCase)) deliveryType = "Normal";
                double deliveryMultiplier = GetDeliveryMultiplier(deliveryType);

                var originCity = WarehouseNetworkService.DetectCity(mainOrigin);
                var destCity = WarehouseNetworkService.DetectCity(mainDest);
                bool hasOriginWarehouse = request.OriginType == "WarehouseDropoff" || WarehouseNetworkService.HasWarehouse(mainOrigin);
                bool hasDestWarehouse = string.IsNullOrWhiteSpace(mainDest) || WarehouseNetworkService.HasWarehouse(mainDest);
                bool isSpecialDelivery = request.IsPrivateTransport || (!string.IsNullOrWhiteSpace(mainDest) && (WarehouseNetworkService.IsSpecialDeliveryCity(mainDest) || !hasDestWarehouse));

                bool isPickupDoorstep = request.OriginType?.Equals("DoorstepPickup", StringComparison.OrdinalIgnoreCase) == true;
                bool isDeliveryDoorstep = request.DestinationType?.Equals("DoorstepDelivery", StringComparison.OrdinalIgnoreCase) == true;

                // Accurate Leg Distances: Between Warehouse Hub and Selected Doorstep Location
                double pickupLegDist = isPickupDoorstep ? GetDoorstepHubDistance(originCity, request.PickupLocation) : 0.0;
                double deliveryLegDist = isDeliveryDoorstep ? GetDoorstepHubDistance(destCity, request.DropoffLocation) : 0.0;
                double flooredDistance = Math.Floor(distanceKm);
                double totalDrivingDist = Math.Round(flooredDistance + pickupLegDist + deliveryLegDist, 1);

                double totalCft = request.TotalCubicFeet;
                if (totalCft <= 0 && request.Length > 0 && request.Width > 0 && request.Height > 0)
                {
                    totalCft = Math.Round(request.Length * request.Width * request.Height * (request.Quantity > 0 ? request.Quantity : 1), 2);
                }
                else if (totalCft <= 0 && !string.IsNullOrWhiteSpace(request.Dimensions))
                {
                    var dims = ParseDimensions(request.Dimensions, request.Quantity);
                    totalCft = dims.cft;
                }
                double volumeMultiplier = GetVolumeMultiplier(totalCft);

                // Exact Excel Rate Calculation Formula:
                double weightCharge = CalculateWeightCharge(weight);
                double distanceCharge = CalculateDistanceCharge(totalDrivingDist > 0 ? totalDrivingDist : flooredDistance);
                double subtotal = (weightCharge + distanceCharge) * deliveryMultiplier * volumeMultiplier;
                double typeSurcharge = subtotal - (weightCharge + distanceCharge);
                double specialDeliverySurcharge = 0.0;
                double total = Math.Floor(subtotal);

                int legCount = (isPickupDoorstep ? 1 : 0) + (isDeliveryDoorstep ? 1 : 0);
                double doorstepFuel = CalculateDoorstepFuelSurcharge(totalCft);
                double doorstepWeight = CalculateDoorstepWeightSurcharge(weight);
                double doorstepPerLeg = doorstepFuel + doorstepWeight;
                double pickupSurcharge = isPickupDoorstep ? doorstepPerLeg : 0.0;
                double deliverySurcharge = isDeliveryDoorstep ? doorstepPerLeg : 0.0;
                double totalDoorstep = pickupSurcharge + deliverySurcharge;

                var pCoord = MapRoutingService.ResolveCityCoordinates(mainOrigin);
                var dCoord = MapRoutingService.ResolveCityCoordinates(mainDest);
                string? fallbackPoly = (pCoord.HasValue && dCoord.HasValue) ? MapRoutingService.EncodePolyline(new[] { pCoord.Value, dCoord.Value }) : null;
                string fallbackDeliveryDays = "5 - 6 Days";
                if (deliveryType.Equals("Fast", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackDeliveryDays = "3 - 4 Days";
                }
                else if (deliveryType.Equals("Express", StringComparison.OrdinalIgnoreCase) || deliveryType.Equals("Urgent", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackDeliveryDays = "1 - 2 Days";
                }

                return new CostEstimateResult
                {
                    DistanceKm = flooredDistance,
                    MainRouteDistanceKm = flooredDistance,
                    PickupLegDistanceKm = pickupLegDist,
                    DeliveryLegDistanceKm = deliveryLegDist,
                    TotalDrivingDistanceKm = totalDrivingDist,
                    ParcelWeight = weight,
                    SingleItemWeight = request.SingleItemWeight > 0 ? request.SingleItemWeight : Math.Round(weight / (request.Quantity > 0 ? request.Quantity : 1), 2),
                    Quantity = request.Quantity > 0 ? request.Quantity : 1,
                    TotalCubicFeet = totalCft,
                    VolumeMultiplier = volumeMultiplier,
                    RatePerKmKg = 2.0,
                    BaseCost = weightCharge,
                    BaseRate = 0.00,
                    WeightCharge = weightCharge,
                    DistanceCharge = distanceCharge,
                    PetrolCharge = distanceCharge,
                    TypeSurcharge = typeSurcharge,
                    DeliveryType = deliveryType,
                    DeliveryMultiplier = deliveryMultiplier,
                    EstimatedDeliveryDays = fallbackDeliveryDays,
                    IsPrivateTransport = isSpecialDelivery,
                    PrivateTransportSurcharge = specialDeliverySurcharge,
                    IsSpecialDelivery = isSpecialDelivery,
                    SpecialDeliverySurcharge = specialDeliverySurcharge,

                    DoorstepLegCount = legCount,
                    DoorstepFuelSurchargePerLeg = doorstepFuel,
                    DoorstepWeightSurchargePerLeg = doorstepWeight,
                    DoorstepSurchargePerLeg = doorstepPerLeg,
                    PickupDoorstepSurcharge = pickupSurcharge,
                    DeliveryDoorstepSurcharge = deliverySurcharge,
                    TotalDoorstepSurcharge = totalDoorstep,

                    OriginCity = originCity,
                    DestinationCity = destCity,
                    HasOriginWarehouse = hasOriginWarehouse,
                    HasDestinationWarehouse = hasDestWarehouse,
                    OriginWarehouseName = originWarehouse?.HubName,
                    DestinationWarehouseName = destWarehouse?.HubName,
                    Currency = "Rs.",
                    EstimatedTotal = total,
                    OriginLat = pCoord?.Lat,
                    OriginLng = pCoord?.Lng,
                    DestinationLat = dCoord?.Lat,
                    DestinationLng = dCoord?.Lng,
                    RoutePolyline = fallbackPoly,
                    Provider = "Fallback Matrix"
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
            var existing = await _context.CostEstimates.FirstOrDefaultAsync(c => c.RequestID == deliveryRequestId);
            if (existing != null)
            {
                existing.DistanceKm = request.DistanceKm;
                existing.ParcelWeight = request.ParcelWeight;
                existing.VehicleType = request.VehicleType;
                existing.RouteType = request.RouteType;
                existing.BaseFare = result.BaseRate;
                existing.DistanceCharge = result.DistanceCharge;
                existing.WeightCharge = result.WeightCharge;
                existing.TypeSurcharge = result.TypeSurcharge;
                existing.EstimatedAmount = result.EstimatedTotal;
                existing.GeneratedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return existing;
            }

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
