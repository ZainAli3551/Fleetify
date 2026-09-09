using System.Threading.Tasks;
using Fleetify.Models.Common;

namespace Fleetify.Services.Interfaces
{
    public interface IMapRoutingService
    {
        Task<RouteDistanceResult> GetDrivingDistanceAsync(string pickup, string dropoff);
        bool IsGoogleMapsConfigured { get; }
    }
}
