using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Fleetify.Data;
using Fleetify.Models.Entities;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class NotificationService : INotificationService
    {
        private readonly FleetifyDbContext _context;

        public NotificationService(FleetifyDbContext context)
        {
            _context = context;
        }

        public async Task<Notification> CreateNotificationAsync(string recipientType, int recipientId, string title, string message, int? relatedEntityId = null)
        {
            var notification = new Notification
            {
                RecipientType = recipientType,
                RecipientID = recipientId,
                Title = title,
                Message = message,
                SentAt = DateTime.UtcNow,
                ReadStatus = "Unread",
                RelatedEntityId = relatedEntityId
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
            return notification;
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(string recipientType, int recipientId, int limit = 10)
        {
            return await _context.Notifications
                .Where(n => n.RecipientType.ToLower() == recipientType.ToLower() && n.RecipientID == recipientId)
                .OrderByDescending(n => n.SentAt)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<bool> MarkAsReadAsync(int notificationId)
        {
            var notif = await _context.Notifications.FindAsync(notificationId);
            if (notif == null) return false;

            notif.ReadStatus = "Read";
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetUnreadCountAsync(string recipientType, int recipientId)
        {
            return await _context.Notifications
                .CountAsync(n => n.RecipientType.ToLower() == recipientType.ToLower() && n.RecipientID == recipientId && n.ReadStatus == "Unread");
        }
    }
}
