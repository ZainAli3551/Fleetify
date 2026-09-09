using System;
using System.ComponentModel.DataAnnotations;

namespace Fleetify.Models.Entities
{
    public abstract class BaseUser
    {
        [Key]
        public int UserID { get; set; }

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(100), EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(25)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Role { get; set; } = "Customer"; // Customer, Admin, Driver

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
