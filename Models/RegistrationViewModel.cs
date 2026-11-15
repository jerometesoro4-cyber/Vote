using System.ComponentModel.DataAnnotations;

namespace voting.Models;

public class RegistrationViewModel : StudentRegistrationViewModel
{
    [Required, Display(Name = "Account type")]
    public string Role { get; set; } = "Student";

    [StringLength(256), DataType(DataType.Password), Display(Name = "Admin invitation code")]
    public string? InvitationCode { get; set; }
}
