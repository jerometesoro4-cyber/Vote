using System.ComponentModel.DataAnnotations;

namespace voting.Models;

public class Candidate
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string Party { get; set; } = "";
    [MaxLength(2000)] public string Platform { get; set; } = "";
    public Position Position { get; set; } = null!;
}
