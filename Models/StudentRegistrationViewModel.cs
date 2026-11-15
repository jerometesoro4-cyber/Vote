
using System.ComponentModel.DataAnnotations;

namespace voting.Models;

public class StudentRegistrationViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Year level")]
    public string? YearLevel { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Use between 12 and 256 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}