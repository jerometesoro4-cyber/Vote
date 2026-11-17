using System.ComponentModel.DataAnnotations;

namespace voting.Models;

public class UserAccount
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? YearLevel { get; set; }

    [MaxLength(100)]
    public string? VotingGroup { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? EmailVerifiedAtUtc { get; set; }

    public int SessionVersion { get; set; } = 1;
}
