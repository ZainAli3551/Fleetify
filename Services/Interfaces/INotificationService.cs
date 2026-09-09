using System.Collections.Generic;
using System.Threading.Tasks;
using Fleetify.Models.Entities;

namespace Fleetify.Services.Interfaces
{
    public interface INotificationService
    {
        Task<Notification> CreateNotificationAsync(string recipientType, int recipientId, string title, string message, int? relatedEntityId = null);
        Task<List<Notification>> GetUserNotificationsAsync(string recipientType, int recipientId, int limit = 10);
        Task<bool> MarkAsReadAsync(int notificationId);
        Task<int> GetUnreadCountAsync(string recipientType, int recipientId);
    }
}
