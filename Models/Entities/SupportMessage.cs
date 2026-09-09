using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fleetify.Models.Entities
{
    public class SupportMessage
    {
        [Key]
        public int MessageID { get; set; }

        [Required]
        public int ConversationID { get; set; }

        /// <summary>
        /// SenderType: "User", "Bot", "Admin"
        /// </summary>
        [Required]
        [MaxLength(30)]
        public string SenderType { get; set; } = "Bot";

        [MaxLength(100)]
        public string SenderName { get; set; } = "Fleetify AI Support";

        [Required]
        public string MessageText { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        [ForeignKey("ConversationID")]
        [System.Text.Json.Serialization.JsonIgnore]
        public virtual SupportConversation? Conversation { get; set; }
    }
}
