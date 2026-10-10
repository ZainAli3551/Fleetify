using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Fleetify.Models.Common;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class MapRoutingService : IMapRoutingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MapRoutingService> _logger;
        private static readonly ConcurrentDictionary<string, RouteDistanceResult> _routeCache = new();

        // Built-in Pakistani Cities & Hubs Coordinate Reference
        public static readonly Dictionary<string, (double Lat, double Lng)> PakistanCities = new(StringComparer.OrdinalIgnoreCase)
        {
            // Specific Landmarks for Hubs & Doorstep Locations
            { "thokar niaz baig", (31.4707, 74.2415) }, // Lahore Central Hub
            { "multan road bypass", (31.4707, 74.2415) },
            { "habib bank chowrangi", (24.8988, 67.0118) }, // Karachi South Hub
            { "s.i.t.e.", (24.8988, 67.0118) },
            { "site industrial area", (24.8988, 67.0118) },
            { "sector i-9", (33.6653, 73.0535) }, // Islamabad North Hub
            { "i-9 industrial area", (33.6653, 73.0535) },
            { "i-9", (33.6653, 73.0535) },
            { "faizabad interchange", (33.6631, 73.0850) }, // Rawalpindi Twin Cities Hub
            { "faizabad", (33.6631, 73.0850) },
            { "murree road", (33.6631, 73.0850) },
            { "millat chowk", (31.4485, 73.0782) }, // Faisalabad Textile Hub
            { "sargodha road", (31.4485, 73.0782) },
            { "khanewal road", (30.2241, 71.5032) }, // Multan South Punjab Hub
            { "gulshan park", (32.1765, 74.1755) }, // Gujranwala GT Road Hub
            { "shaheenabad", (32.1765, 74.1755) },
            { "hayatabad industrial estate", (33.9788, 71.4282) }, // Peshawar Khyber Hub
            { "phase 7", (33.9788, 71.4282) },
            { "hayatabad", (33.9788, 71.4282) },
            { "sambrial", (32.4820, 74.3540) }, // Sialkot Export Hub
            { "wazirabad road", (32.4820, 74.3540) },
            { "zarghoon road", (30.2012, 67.0185) }, // Quetta Balochistan Hub
            { "quetta cantt", (30.2012, 67.0185) },
            { "pakpattan chowk", (30.6652, 73.1114) }, // Sahiwal Special Delivery
            { "general bus stand", (30.8120, 73.4510) }, // Okara Special Delivery
            { "steel bagh", (31.1190, 74.4530) }, // Kasur Special Delivery
            { "military road", (27.7120, 68.8450) }, // Sukkur Special Delivery
            { "sukkur bypass", (27.7120, 68.8450) },

            // Major Neighborhoods & Sectors for Doorstep Delivery Resolution
            { "model town gujranwala", (32.1725, 74.1950) },
            { "satellite town gujranwala", (32.1645, 74.2085) },
            { "peoples colony gujranwala", (32.1485, 74.2050) },
            { "dc colony", (32.2285, 74.1520) },
            { "rahwali cantt", (32.2450, 74.1380) },
            { "rahwali", (32.2450, 74.1380) },
            { "citi housing gujranwala", (32.2350, 74.1350) },
            { "master city", (32.1250, 74.2250) },
            { "fazaia housing", (32.2650, 74.1250) },
            { "civil lines gujranwala", (32.1550, 74.1880) },
            { "wapda town gujranwala", (32.2050, 74.1550) },
            { "gulberg", (31.5135, 74.3444) },
            { "model town lahore", (31.4826, 74.3184) },
            { "model town", (31.4826, 74.3184) },
            { "dha phase 5", (31.4550, 74.4150) },
            { "dha phase 6", (31.4420, 74.4550) },
            { "dha phase", (31.4697, 74.3986) },
            { "dha", (31.4697, 74.3986) },
            { "defence", (31.4697, 74.3986) },
            { "johar town", (31.4697, 74.2728) },
            { "bahria town lahore", (31.3688, 74.1804) },
            { "bahria town", (31.3688, 74.1804) },
            { "wapda town", (31.4335, 74.2586) },
            { "faisal town", (31.4756, 74.3056) },
            { "allama iqbal town", (31.5126, 74.2874) },
            { "iqbal town", (31.5126, 74.2874) },
            { "shadman", (31.5412, 74.3315) },
            { "cavalry ground", (31.5035, 74.3725) },
            { "township", (31.4425, 74.3075) },
            { "valencia", (31.4050, 74.2550) },
            { "garden town", (31.5050, 74.3250) },
            { "raiwind road", (31.3850, 74.2050) },
            { "blue area", (33.7125, 73.0650) },
            { "f-6", (33.7295, 73.0760) },
            { "f-7", (33.7215, 73.0550) },
            { "f-8", (33.7115, 73.0350) },
            { "f-10", (33.6935, 73.0050) },
            { "f-11", (33.6835, 72.9850) },
            { "g-6", (33.7055, 73.0850) },
            { "g-7", (33.6985, 73.0650) },
            { "g-8", (33.6895, 73.0450) },
            { "g-9", (33.6825, 73.0250) },
            { "g-10", (33.6755, 73.0050) },
            { "g-11", (33.6685, 72.9850) },
            { "i-8", (33.6705, 73.0750) },
            { "i-10", (33.6555, 73.0350) },
            { "saddar rawalpindi", (33.5950, 73.0550) },
            { "westridge", (33.6050, 73.0250) },
            { "commercial market", (33.6380, 73.0720) },
            { "d ground", (31.4085, 73.1050) },
            { "madina town", (31.4285, 73.1250) },
            { "gulgasht", (30.2280, 71.4750) },
            { "bosan road", (30.2350, 71.4650) },
            { "clifton", (24.8150, 67.0450) },
            { "dha karachi", (24.8150, 67.0650) },
            { "gulshan-e-iqbal", (24.9250, 67.0950) },
            { "north nazimabad", (24.9450, 67.0450) },
            { "korangi", (24.8350, 67.1350) },

            // City Centers
            { "lahore", (31.5204, 74.3587) },
            { "karachi", (24.8607, 67.0011) },
            { "islamabad", (33.6844, 73.0479) },
            { "rawalpindi", (33.5651, 73.0169) },
            { "faisalabad", (31.4504, 73.1350) },
            { "multan", (30.1575, 71.5249) },
            { "gujranwala", (32.1877, 74.1945) },
            { "peshawar", (34.0151, 71.5249) },
            { "quetta", (30.1798, 66.9750) },
            { "sialkot", (32.4945, 74.5229) },
            { "kasur", (31.1179, 74.4506) },
            { "murree", (33.9070, 73.3943) },
            { "abbottabad", (34.1688, 73.2215) },
            { "hyderabad", (25.3960, 68.3578) },
            { "bahawalpur", (29.3956, 71.6836) },
            { "sargodha", (32.0836, 72.6711) },
            { "sukkur", (27.7052, 68.8574) },
            { "jhelum", (32.9425, 73.7257) },
            { "gujrat", (32.5742, 74.0754) },
            { "sheikhupura", (31.7131, 73.9783) },
            { "sahiwal", (30.6682, 73.1114) },
            { "okara", (30.8081, 73.4458) },
            { "rahim yar khan", (28.4212, 70.2989) },
            { "larkana", (27.5590, 68.2120) },
            { "mardan", (34.1989, 72.0450) },
            { "swat", (34.7758, 72.3626) },
            { "mingora", (34.7758, 72.3626) },
            { "muzaffarabad", (34.3700, 73.4711) },
            { "mirpur", (33.1478, 73.7519) },
            { "gwadar", (25.1216, 62.3254) }
        };

        public MapRoutingService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<MapRoutingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FleetifyApp/1.0 (Logistics Platform)");
        }

        public bool IsGoogleMapsConfigured
        {
            get
            {
                var key = _configuration["GoogleMaps:ApiKey"];
                return !string.IsNullOrWhiteSpace(key);
            }
        }

        public async Task<RouteDistanceResult> GetDrivingDistanceAsync(string pickup, string dropoff)
        {
            pickup = pickup?.Trim() ?? string.Empty;
            dropoff = dropoff?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(pickup) || string.IsNullOrWhiteSpace(dropoff))
            {
                return new RouteDistanceResult
                {
                    Success = false,
                    DistanceKm = 15.0,
                    DurationMinutes = 30,
                    Origin = pickup,
                    Destination = dropoff,
                    Provider = "Fallback",
                    ErrorMessage = "Pickup or dropoff address is empty."
                };
            }

            string cacheKey = $"{pickup.ToLowerInvariant()}|{dropoff.ToLowerInvariant()}";
            if (_routeCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            // 1. Try Google Maps Routes API (New) if key is present
            string? googleKey = _configuration["GoogleMaps:ApiKey"];
            if (!string.IsNullOrWhiteSpace(googleKey))
            {
                try
                {
                    var googleResult = await CalculateGoogleRoutesAsync(pickup, dropoff, googleKey);
                    if (googleResult.Success)
                    {
                        _routeCache.TryAdd(cacheKey, googleResult);
                        return googleResult;
                    }
                    _logger.LogWarning("Google Routes API failed: {Error}. Falling back to OpenStreetMap OSRM.", googleResult.ErrorMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Google Routes API call failed. Falling back to OpenStreetMap OSRM.");
                }
            }

            // 2. Fallback to OpenStreetMap (Nominatim Geocode + OSRM Driving Engine)
            try
            {
                var osrmResult = await CalculateOsrmAsync(pickup, dropoff);
                if (osrmResult.Success)
                {
                    _routeCache.TryAdd(cacheKey, osrmResult);
                    return osrmResult;
                }
                _logger.LogWarning("OSRM engine failed: {Error}. Falling back to local route matrix.", osrmResult.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSRM routing failed. Falling back to local matrix.");
            }

            // 3. Deterministic Local Matrix Fallback
            var localFallback = CalculateLocalMatrix(pickup, dropoff);
            _routeCache.TryAdd(cacheKey, localFallback);
            return localFallback;
        }

        private async Task<RouteDistanceResult> CalculateGoogleRoutesAsync(string pickup, string dropoff, string apiKey)
        {
            string url = "https://routes.googleapis.com/directions/v2:computeRoutes";

            var payload = new
            {
                origin = new { address = pickup },
                destination = new { address = dropoff },
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_UNAWARE",
                computeAlternativeRoutes = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline,routes.legs.startLocation,routes.legs.endLocation");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new RouteDistanceResult
                {
                    Success = false,
                    ErrorMessage = $"Google Routes HTTP {response.StatusCode}: {responseJson}"
                };
            }

            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
            {
                var firstRoute = routes[0];
                int distanceMeters = firstRoute.GetProperty("distanceMeters").GetInt32();
                string durationStr = firstRoute.GetProperty("duration").GetString() ?? "0s";

                int durationSeconds = 0;
                var durMatch = Regex.Match(durationStr, @"(\d+)s");
                if (durMatch.Success && int.TryParse(durMatch.Groups[1].Value, out int sec))
                {
                    durationSeconds = sec;
                }

                string? polyline = null;
                if (firstRoute.TryGetProperty("polyline", out var poly) && poly.TryGetProperty("encodedPolyline", out var enc))
                {
                    polyline = enc.GetString();
                }

                double? originLat = null;
                double? originLng = null;
                double? destLat = null;
                double? destLng = null;

                if (firstRoute.TryGetProperty("legs", out var legs) && legs.GetArrayLength() > 0)
                {
                    var firstLeg = legs[0];
                    if (firstLeg.TryGetProperty("startLocation", out var sLoc) && sLoc.TryGetProperty("latLng", out var sLatLng))
                    {
                        if (sLatLng.TryGetProperty("latitude", out var latProp)) originLat = latProp.GetDouble();
                        if (sLatLng.TryGetProperty("longitude", out var lngProp)) originLng = lngProp.GetDouble();
                    }
                    var lastLeg = legs[legs.GetArrayLength() - 1];
                    if (lastLeg.TryGetProperty("endLocation", out var eLoc) && eLoc.TryGetProperty("latLng", out var eLatLng))
                    {
                        if (eLatLng.TryGetProperty("latitude", out var latProp)) destLat = latProp.GetDouble();
                        if (eLatLng.TryGetProperty("longitude", out var lngProp)) destLng = lngProp.GetDouble();
                    }
                }

                // Fallback coordinates from Pakistani cities dictionary if Google legs are omitted
                if (originLat == null || originLng == null)
                {
                    var pCoord = ResolveCityCoordinates(pickup);
                    if (pCoord.HasValue) { originLat = pCoord.Value.Lat; originLng = pCoord.Value.Lng; }
                }
                if (destLat == null || destLng == null)
                {
                    var dCoord = ResolveCityCoordinates(dropoff);
                    if (dCoord.HasValue) { destLat = dCoord.Value.Lat; destLng = dCoord.Value.Lng; }
                }

                // If polyline was somehow omitted, generate clean two-point polyline
                if (string.IsNullOrWhiteSpace(polyline) && originLat.HasValue && originLng.HasValue && destLat.HasValue && destLng.HasValue)
                {
                    polyline = EncodePolyline(new[] { (originLat.Value, originLng.Value), (destLat.Value, destLng.Value) });
                }

                double distanceKm = Math.Round(distanceMeters / 1000.0, 1);
                int durationMinutes = Math.Max(1, (int)Math.Round(durationSeconds / 60.0));

                return new RouteDistanceResult
                {
                    Success = true,
                    DistanceKm = distanceKm,
                    DurationMinutes = durationMinutes,
                    Origin = pickup,
                    Destination = dropoff,
                    OriginLat = originLat,
                    OriginLng = originLng,
                    DestinationLat = destLat,
                    DestinationLng = destLng,
                    EncodedPolyline = polyline,
                    Provider = "Google Maps"
                };
            }

            return new RouteDistanceResult
            {
                Success = false,
                ErrorMessage = "Google Routes API returned no routes."
            };
        }

        private async Task<RouteDistanceResult> CalculateOsrmAsync(string pickup, string dropoff)
        {
            // Geocode Pickup & Dropoff via Nominatim with fast fallback to dictionary
            var pCoord = await GeocodeNominatimAsync(pickup) ?? ResolveCityCoordinates(pickup);
            var dCoord = await GeocodeNominatimAsync(dropoff) ?? ResolveCityCoordinates(dropoff);

            if (pCoord == null || dCoord == null)
            {
                return new RouteDistanceResult
                {
                    Success = false,
                    ErrorMessage = "Unable to geocode pickup or dropoff location."
                };
            }

            // OSRM Driving Route
            string osrmUrl = $"http://router.project-osrm.org/route/v1/driving/{pCoord.Value.Lng:F6},{pCoord.Value.Lat:F6};{dCoord.Value.Lng:F6},{dCoord.Value.Lat:F6}?overview=simplified";
            var osrmResponse = await _httpClient.GetAsync(osrmUrl);

            if (!osrmResponse.IsSuccessStatusCode)
            {
                return new RouteDistanceResult
                {
                    Success = false,
                    ErrorMessage = $"OSRM HTTP {osrmResponse.StatusCode}"
                };
            }

            string osrmJson = await osrmResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(osrmJson);
            if (doc.RootElement.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
            {
                var r = routes[0];
                double meters = r.GetProperty("distance").GetDouble();
                double seconds = r.GetProperty("duration").GetDouble();
                string? geometry = null;
                if (r.TryGetProperty("geometry", out var geom))
                {
                    geometry = geom.GetString();
                }

                if (string.IsNullOrWhiteSpace(geometry))
                {
                    geometry = EncodePolyline(new[] { pCoord.Value, dCoord.Value });
                }

                return new RouteDistanceResult
                {
                    Success = true,
                    DistanceKm = Math.Round(meters / 1000.0, 1),
                    DurationMinutes = Math.Max(1, (int)Math.Round(seconds / 60.0)),
                    Origin = pickup,
                    Destination = dropoff,
                    OriginLat = pCoord.Value.Lat,
                    OriginLng = pCoord.Value.Lng,
                    DestinationLat = dCoord.Value.Lat,
                    DestinationLng = dCoord.Value.Lng,
                    EncodedPolyline = geometry,
                    Provider = "OSRM Road Engine"
                };
            }

            return new RouteDistanceResult
            {
                Success = false,
                ErrorMessage = "OSRM returned no routes."
            };
        }

        private async Task<(double Lat, double Lng)?> GeocodeNominatimAsync(string query)
        {
            try
            {
                string search = query;
                if (!search.Contains("pakistan", StringComparison.OrdinalIgnoreCase))
                {
                    search += ", Pakistan";
                }

                string encoded = Uri.EscapeDataString(search);
                string url = $"https://nominatim.openstreetmap.org/search?q={encoded}&countrycodes=pk&format=json&limit=1";

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(4));
                var res = await _httpClient.GetAsync(url, cts.Token);
                if (!res.IsSuccessStatusCode) return null;

                string json = await res.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetArrayLength() > 0)
                {
                    var item = doc.RootElement[0];
                    if (item.TryGetProperty("lat", out var latProp) && item.TryGetProperty("lon", out var lonProp) &&
                        double.TryParse(latProp.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(lonProp.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lng))
                    {
                        return (lat, lng);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nominatim geocoding timeout or error for {Query}", query);
            }
            return null;
        }

        public RouteDistanceResult CalculateLocalMatrix(string pickup, string dropoff)
        {
            var p = pickup.ToLowerInvariant();
            var d = dropoff.ToLowerInvariant();
            double km = 0;

            if ((p.Contains("gujranwala") && d.Contains("lahore")) || (p.Contains("lahore") && d.Contains("gujranwala"))) km = 71.0;
            else if ((p.Contains("lahore") && d.Contains("karachi")) || (p.Contains("karachi") && d.Contains("lahore"))) km = 1215.0;
            else if ((p.Contains("lahore") && d.Contains("islamabad")) || (p.Contains("islamabad") && d.Contains("lahore"))) km = 375.0;
            else if ((p.Contains("gujranwala") && d.Contains("islamabad")) || (p.Contains("islamabad") && d.Contains("gujranwala"))) km = 215.0;
            else if ((p.Contains("gujranwala") && d.Contains("sialkot")) || (p.Contains("sialkot") && d.Contains("gujranwala"))) km = 52.0;
            else if ((p.Contains("lahore") && d.Contains("kasur")) || (p.Contains("kasur") && d.Contains("lahore"))) km = 55.0;
            else if ((p.Contains("islamabad") && d.Contains("rawalpindi")) || (p.Contains("rawalpindi") && d.Contains("islamabad"))) km = 18.0;
            else if ((p.Contains("lahore") && d.Contains("faisalabad")) || (p.Contains("faisalabad") && d.Contains("lahore"))) km = 180.0;
            else if ((p.Contains("lahore") && d.Contains("multan")) || (p.Contains("multan") && d.Contains("lahore"))) km = 345.0;
            else if ((p.Contains("islamabad") && d.Contains("peshawar")) || (p.Contains("peshawar") && d.Contains("islamabad"))) km = 185.0;
            else if ((p.Contains("karachi") && d.Contains("hyderabad")) || (p.Contains("hyderabad") && d.Contains("karachi"))) km = 160.0;

            var originCoord = ResolveCityCoordinates(pickup) ?? (31.5204, 74.3587); // Lahore default
            var destCoord = ResolveCityCoordinates(dropoff) ?? (33.6844, 73.0479); // Islamabad default

            if (km <= 0)
            {
                double haversine = CalculateHaversineDistance(originCoord.Lat, originCoord.Lng, destCoord.Lat, destCoord.Lng);
                if (haversine > 0.1)
                {
                    km = Math.Round(haversine * 1.28, 1); // 28% road curvature
                }
                else
                {
                    int seed = Math.Abs((p + "|" + d).GetHashCode());
                    km = 2.5 + (seed % 10);
                }
            }

            int minutes = Math.Max(1, (int)Math.Round(km * 1.15));
            string polyline = EncodePolyline(new[] { originCoord, destCoord });

            return new RouteDistanceResult
            {
                Success = true,
                DistanceKm = Math.Round(km, 1),
                DurationMinutes = minutes,
                Origin = pickup,
                Destination = dropoff,
                OriginLat = originCoord.Lat,
                OriginLng = originCoord.Lng,
                DestinationLat = destCoord.Lat,
                DestinationLng = destCoord.Lng,
                EncodedPolyline = polyline,
                Provider = "Fleetify Route Engine"
            };
        }

        public static (double Lat, double Lng)? ResolveCityCoordinates(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var clean = address.ToLowerInvariant();
            foreach (var kvp in PakistanCities)
            {
                if (clean.Contains(kvp.Key))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        public static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Earth radius in km
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double angle) => (Math.PI / 180) * angle;

        public static string EncodePolyline(IEnumerable<(double Lat, double Lng)> points)
        {
            var str = new StringBuilder();
            void EncodeDiff(int diff)
            {
                int shifted = diff < 0 ? ~(diff << 1) : (diff << 1);
                while (shifted >= 0x20)
                {
                    str.Append((char)((0x20 | (shifted & 0x1f)) + 63));
                    shifted >>= 5;
                }
                str.Append((char)(shifted + 63));
            }

            int lastLat = 0;
            int lastLng = 0;
            foreach (var pt in points)
            {
                int lat = (int)Math.Round(pt.Lat * 1e5);
                int lng = (int)Math.Round(pt.Lng * 1e5);
                EncodeDiff(lat - lastLat);
                EncodeDiff(lng - lastLng);
                lastLat = lat;
                lastLng = lng;
            }
            return str.ToString();
        }
    }
}
