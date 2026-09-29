using System;
using System.Collections.Generic;
using System.Linq;

namespace Fleetify.Services
{
    public class WarehouseInfo
    {
        public string City { get; set; } = string.Empty;
        public string HubName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string OperatingHours { get; set; } = "08:00 AM - 09:00 PM";
    }

    public static class WarehouseNetworkService
    {
        private static readonly Dictionary<string, WarehouseInfo> Warehouses = new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "Lahore",
                new WarehouseInfo
                {
                    City = "Lahore",
                    HubName = "Fleetify Central Hub - Lahore",
                    Address = "Multan Road, Near Thokar Niaz Baig, Lahore",
                    ContactNumber = "+92 42 35910001",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Karachi",
                new WarehouseInfo
                {
                    City = "Karachi",
                    HubName = "Fleetify South Hub - Karachi",
                    Address = "Plot 42, Korangi Industrial Area, Karachi",
                    ContactNumber = "+92 21 35010002",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Islamabad",
                new WarehouseInfo
                {
                    City = "Islamabad",
                    HubName = "Fleetify North Hub - Islamabad",
                    Address = "Sector I-9/2 Industrial Area, Islamabad",
                    ContactNumber = "+92 51 44310003",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Rawalpindi",
                new WarehouseInfo
                {
                    City = "Rawalpindi",
                    HubName = "Fleetify Twin Cities Hub - Rawalpindi",
                    Address = "Westridge 1, Main Peshawar Road, Rawalpindi",
                    ContactNumber = "+92 51 54610004",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Faisalabad",
                new WarehouseInfo
                {
                    City = "Faisalabad",
                    HubName = "Fleetify Textile Hub - Faisalabad",
                    Address = "Small Industrial Estate, Sargodha Road, Faisalabad",
                    ContactNumber = "+92 41 87110005",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Multan",
                new WarehouseInfo
                {
                    City = "Multan",
                    HubName = "Fleetify South Punjab Hub - Multan",
                    Address = "Industrial Estate Phase 2, Khanewal Road, Multan",
                    ContactNumber = "+92 61 65210006",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Peshawar",
                new WarehouseInfo
                {
                    City = "Peshawar",
                    HubName = "Fleetify Khyber Hub - Peshawar",
                    Address = "Industrial Estate, Jamrud Road, Hayatabad, Peshawar",
                    ContactNumber = "+92 91 58110007",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Sialkot",
                new WarehouseInfo
                {
                    City = "Sialkot",
                    HubName = "Fleetify Export Hub - Sialkot",
                    Address = "Sambrial Road, Near Dry Port, Sialkot",
                    ContactNumber = "+92 52 35510008",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Gujranwala",
                new WarehouseInfo
                {
                    City = "Gujranwala",
                    HubName = "Fleetify GT Road Hub - Gujranwala",
                    Address = "Climax Town, Main GT Road, Gujranwala",
                    ContactNumber = "+92 55 38410009",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Quetta",
                new WarehouseInfo
                {
                    City = "Quetta",
                    HubName = "Fleetify Balochistan Hub - Quetta",
                    Address = "Joint Road, Near Railway Station, Quetta",
                    ContactNumber = "+92 81 28210010",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            }
        };

        public static IReadOnlyList<WarehouseInfo> GetAllWarehouses()
        {
            return Warehouses.Values.ToList();
        }

        public static string DetectCity(string? location)
        {
            if (string.IsNullOrWhiteSpace(location)) return string.Empty;

            var loc = location.Trim();

            // Direct check against known warehouse cities
            foreach (var city in Warehouses.Keys)
            {
                if (loc.Contains(city, StringComparison.OrdinalIgnoreCase))
                {
                    return city;
                }
            }

            // Check comma separated parts (e.g. "Main Market, Gulberg, Lahore" -> "Lahore")
            var parts = loc.Split(new[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var candidate = parts[^1].Trim();
                foreach (var city in Warehouses.Keys)
                {
                    if (candidate.Equals(city, StringComparison.OrdinalIgnoreCase) ||
                        candidate.Contains(city, StringComparison.OrdinalIgnoreCase))
                    {
                        return city;
                    }
                }
                return candidate;
            }

            return loc;
        }

        public static bool HasWarehouse(string? cityOrLocation)
        {
            var detected = DetectCity(cityOrLocation);
            return !string.IsNullOrWhiteSpace(detected) && Warehouses.ContainsKey(detected);
        }

        public static readonly string[] SpecialDeliveryCities = new[] { "Sahiwal", "Okara", "Kasur", "Kasoor", "Sukkur" };

        public static bool IsSpecialDeliveryCity(string? cityOrLocation)
        {
            if (string.IsNullOrWhiteSpace(cityOrLocation)) return false;
            var loc = cityOrLocation.Trim();
            foreach (var city in SpecialDeliveryCities)
            {
                if (loc.Contains(city, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return !HasWarehouse(cityOrLocation);
        }

        public static WarehouseInfo? GetWarehouse(string? cityOrLocation)
        {
            var detected = DetectCity(cityOrLocation);
            if (!string.IsNullOrWhiteSpace(detected) && Warehouses.TryGetValue(detected, out var info))
            {
                return info;
            }
            return null;
        }
    }
}
