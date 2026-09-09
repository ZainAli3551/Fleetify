using System.ComponentModel.DataAnnotations;

namespace Fleetify.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email is required"), EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required"), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public string Role { get; set; } = "Customer"; // Customer, Admin, Driver

        public string? ReturnUrl { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Full Name is required"), MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required"), EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required"), DataType(DataType.Password), MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone Number is required"), Phone]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Customer"; // Customer, Admin, Driver

        // Optional address for Customer
        public string? Address { get; set; }

        // Optional license for Driver
        public string? LicenseNumber { get; set; }
    }
}
