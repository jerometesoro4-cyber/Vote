using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace voting.Models;

public class EmailVerificationCode
{
    public const string PurposeVerifyEmail = "VerifyEmail";
    public const string PurposeResetPassword = "ResetPassword";

    public int Id { get; set; }
    public int UserAccountId { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
    [Required, MaxLength(32)] public string Purpose { get; set; } = "";
    [Required, MaxLength(64)] public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;

    public static string HashCode(int userAccountId, string purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userAccountId}:{purpose}:{code.Trim()}")));

    public static bool VerifyCode(int userAccountId, string purpose, string code, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actualBytes = Encoding.UTF8.GetBytes(HashCode(userAccountId, purpose, code));
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}

public class VerifyEmailViewModel
{
    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = "";

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit verification code.")]
    [StringLength(6, MinimumLength = 6)]
    [Display(Name = "Verification code")]
    public string Code { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ResetPasswordViewModel
{
    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = "";

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit recovery code.")]
    [StringLength(6, MinimumLength = 6)]
    [Display(Name = "Recovery code")]
    public string Code { get; set; } = "";

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Use between 12 and 256 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
