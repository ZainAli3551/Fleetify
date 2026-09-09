using System;
using System.ComponentModel.DataAnnotations;

namespace Fleetify.Models.Entities
{
    public class Notification
    {
        [Key]
        public int NotificationID { get; set; }

        [Required, MaxLength(20)]
        public string RecipientType { get; set; } = "Customer"; // Customer, Driver, Admin

        public int RecipientID { get; set; }

        [Required, MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(20)]
        public string ReadStatus { get; set; } = "Unread"; // Unread, Read

        public int? RelatedEntityId { get; set; }
    }
}
