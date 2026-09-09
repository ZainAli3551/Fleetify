using System;
using System.Collections.Concurrent;
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
            request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline");
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

                double distanceKm = Math.Round(distanceMeters / 1000.0, 1);
                int durationMinutes = Math.Max(1, (int)Math.Round(durationSeconds / 60.0));

                return new RouteDistanceResult
                {
                    Success = true,
                    DistanceKm = distanceKm,
                    DurationMinutes = durationMinutes,
                    Origin = pickup,
                    Destination = dropoff,
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
            // Geocode Pickup via Nominatim
            var pCoord = await GeocodeNominatimAsync(pickup);
            var dCoord = await GeocodeNominatimAsync(dropoff);

            if (pCoord == null || dCoord == null)
            {
                return new RouteDistanceResult
                {
                    Success = false,
                    ErrorMessage = "Unable to geocode pickup or dropoff location via Nominatim."
                };
            }

            // OSRM Driving Route
            string osrmUrl = $"http://router.project-osrm.org/route/v1/driving/{pCoord.Value.lng:F6},{pCoord.Value.lat:F6};{dCoord.Value.lng:F6},{dCoord.Value.lat:F6}?overview=simplified";
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

                return new RouteDistanceResult
                {
                    Success = true,
                    DistanceKm = Math.Round(meters / 1000.0, 1),
                    DurationMinutes = Math.Max(1, (int)Math.Round(seconds / 60.0)),
                    Origin = pickup,
                    Destination = dropoff,
                    OriginLat = pCoord.Value.lat,
                    OriginLng = pCoord.Value.lng,
                    DestinationLat = dCoord.Value.lat,
                    DestinationLng = dCoord.Value.lng,
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

        private async Task<(double lat, double lng)?> GeocodeNominatimAsync(string query)
        {
            try
            {
                string encoded = Uri.EscapeDataString(query);
                string url = $"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=1";
                
                var res = await _httpClient.GetAsync(url);
                if (!res.IsSuccessStatusCode) return null;

                string json = await res.Content.ReadAsStringAsync();
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
                _logger.LogWarning(ex, "Nominatim geocoding error for {Query}", query);
            }
            return null;
        }

        private RouteDistanceResult CalculateLocalMatrix(string pickup, string dropoff)
        {
            var p = pickup.ToLower();
            var d = dropoff.ToLower();
            double km = 15.0;

            if ((p.Contains("gujranwala") && d.Contains("lahore")) || (p.Contains("lahore") && d.Contains("gujranwala"))) km = 71.0;
            else if ((p.Contains("lahore") && d.Contains("karachi")) || (p.Contains("karachi") && d.Contains("lahore"))) km = 1215.0;
            else if ((p.Contains("lahore") && d.Contains("islamabad")) || (p.Contains("islamabad") && d.Contains("lahore"))) km = 375.0;
            else if ((p.Contains("gujranwala") && d.Contains("islamabad")) || (p.Contains("islamabad") && d.Contains("gujranwala"))) km = 215.0;
            else if ((p.Contains("gujranwala") && d.Contains("sialkot")) || (p.Contains("sialkot") && d.Contains("gujranwala"))) km = 52.0;
            else
            {
                int seed = Math.Abs((p + "|" + d).GetHashCode());
                km = 12.0 + (seed % 45);
            }

            int minutes = Math.Max(1, (int)Math.Round(km * 1.15));

            return new RouteDistanceResult
            {
                Success = true,
                DistanceKm = Math.Round(km, 1),
                DurationMinutes = minutes,
                Origin = pickup,
                Destination = dropoff,
                Provider = "Local Matrix Fallback"
            };
        }
    }
}
