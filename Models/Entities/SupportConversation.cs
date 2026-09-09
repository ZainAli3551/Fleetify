using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class SupportConversation
    {
        [Key]
        public int ConversationID { get; set; }

        [Required]
        [MaxLength(100)]
        public string SessionToken { get; set; } = Guid.NewGuid().ToString("N");

        public int? UserID { get; set; }

        [MaxLength(100)]
        public string UserName { get; set; } = "Guest User";

        [MaxLength(100)]
        public string UserEmail { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Subject { get; set; } = "General Support Request";

        /// <summary>
        /// Status: "BotHandled", "NeedsAdmin", "InProgress", "Resolved", "Closed"
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "BotHandled";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? AdminNotes { get; set; }

        // Navigation Properties
        [ForeignKey("UserID")]
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual User? User { get; set; }

        public virtual ICollection<SupportMessage> Messages { get; set; } = new List<SupportMessage>();
    }
}
