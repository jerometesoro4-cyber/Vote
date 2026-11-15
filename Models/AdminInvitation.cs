using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace voting.Models;

public class AdminInvitation
{
    public int Id { get; set; }
    [Required, MaxLength(64)] public string CodeHash { get; set; } = "";
    public int CreatorId { get; set; }
    public UserAccount Creator { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));
}

public class AdminInvitationsViewModel
{
    public List<AdminInvitation> Invitations { get; set; } = [];
}
