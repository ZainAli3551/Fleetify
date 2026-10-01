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
        public string DoorstepLandmark { get; set; } = string.Empty;
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
                    HubName = "Fleetify Central Hub – Lahore: Thokar Niaz Baig, near Multan Road Bypass",
                    Address = "Thokar Niaz Baig, near Multan Road Bypass, Lahore",
                    DoorstepLandmark = "Thokar Niaz Baig, near Multan Road Bypass, Lahore",
                    ContactNumber = "+92 42 35910001",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Karachi",
                new WarehouseInfo
                {
                    City = "Karachi",
                    HubName = "Fleetify South Hub – Karachi: S.I.T.E. Industrial Area, near Habib Bank Chowrangi",
                    Address = "S.I.T.E. Industrial Area, near Habib Bank Chowrangi, Karachi",
                    DoorstepLandmark = "S.I.T.E. Industrial Area, near Habib Bank Chowrangi, Karachi",
                    ContactNumber = "+92 21 35010002",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Islamabad",
                new WarehouseInfo
                {
                    City = "Islamabad",
                    HubName = "Fleetify North Hub – Islamabad: I-9 Industrial Area, near Sector I-9/3",
                    Address = "I-9 Industrial Area, near Sector I-9/3, Islamabad",
                    DoorstepLandmark = "I-9 Industrial Area, near Sector I-9/3, Islamabad",
                    ContactNumber = "+92 51 44310003",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Rawalpindi",
                new WarehouseInfo
                {
                    City = "Rawalpindi",
                    HubName = "Fleetify Twin Cities Hub – Rawalpindi: Murree Road, near Faizabad Interchange",
                    Address = "Murree Road, near Faizabad Interchange, Rawalpindi",
                    DoorstepLandmark = "Murree Road, near Faizabad Interchange, Rawalpindi",
                    ContactNumber = "+92 51 54610004",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Faisalabad",
                new WarehouseInfo
                {
                    City = "Faisalabad",
                    HubName = "Fleetify Textile Hub – Faisalabad: Sargodha Road, near Millat Chowk",
                    Address = "Sargodha Road, near Millat Chowk, Faisalabad",
                    DoorstepLandmark = "Sargodha Road, near Millat Chowk, Faisalabad",
                    ContactNumber = "+92 41 87110005",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Multan",
                new WarehouseInfo
                {
                    City = "Multan",
                    HubName = "Fleetify South Punjab Hub – Multan: Khanewal Road, near Industrial Estate",
                    Address = "Khanewal Road, near Industrial Estate, Multan",
                    DoorstepLandmark = "Khanewal Road, near Industrial Estate, Multan",
                    ContactNumber = "+92 61 65210006",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Peshawar",
                new WarehouseInfo
                {
                    City = "Peshawar",
                    HubName = "Fleetify Khyber Hub – Peshawar: Hayatabad Industrial Estate, near Phase 7",
                    Address = "Hayatabad Industrial Estate, near Phase 7, Peshawar",
                    DoorstepLandmark = "Hayatabad Industrial Estate, near Phase 7, Peshawar",
                    ContactNumber = "+92 91 58110007",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Sialkot",
                new WarehouseInfo
                {
                    City = "Sialkot",
                    HubName = "Fleetify Export Hub – Sialkot: Wazirabad Road, near Sambrial",
                    Address = "Wazirabad Road, near Sambrial, Sialkot",
                    DoorstepLandmark = "Wazirabad Road, near Sambrial, Sialkot",
                    ContactNumber = "+92 52 35510008",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Gujranwala",
                new WarehouseInfo
                {
                    City = "Gujranwala",
                    HubName = "Fleetify GT Road Hub – Gujranwala: Shaheenabad, near Gulshan Park / GT Road",
                    Address = "Shaheenabad, near Gulshan Park / GT Road, Gujranwala",
                    DoorstepLandmark = "Shaheenabad, near Gulshan Park / GT Road, Gujranwala",
                    ContactNumber = "+92 55 38410009",
                    OperatingHours = "08:00 AM - 09:00 PM"
                }
            },
            {
                "Quetta",
                new WarehouseInfo
                {
                    City = "Quetta",
                    HubName = "Fleetify Balochistan Hub – Quetta: Zarghoon Road, near Quetta Cantt",
                    Address = "Zarghoon Road, near Quetta Cantt, Quetta",
                    DoorstepLandmark = "Zarghoon Road, near Quetta Cantt, Quetta",
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

        public static readonly Dictionary<string, string> SpecialDeliveryLandmarks = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Sahiwal", "GT Road, near Pakpattan Chowk, Sahiwal" },
            { "Okara", "M.A. Jinnah Road, near General Bus Stand, Okara" },
            { "Kasur", "Railway Road, near Steel Bagh, Kasur" },
            { "Kasoor", "Railway Road, near Steel Bagh, Kasur" },
            { "Sukkur", "Military Road, near Sukkur Bypass, Sukkur" }
        };

        public static string GetDoorstepLandmark(string? cityOrLocation)
        {
            var wh = GetWarehouse(cityOrLocation);
            if (wh != null && !string.IsNullOrWhiteSpace(wh.DoorstepLandmark))
            {
                return wh.DoorstepLandmark;
            }

            if (!string.IsNullOrWhiteSpace(cityOrLocation))
            {
                foreach (var kvp in SpecialDeliveryLandmarks)
                {
                    if (cityOrLocation.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value;
                    }
                }
            }

            return cityOrLocation ?? string.Empty;
        }
    }
}
