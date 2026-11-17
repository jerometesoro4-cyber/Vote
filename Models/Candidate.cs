using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace voting.Models;

public class Candidate
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string Party { get; set; } = "";
    [MaxLength(2000)] public string Platform { get; set; } = "";
    [MaxLength(120)] public string? PhotoFileName { get; set; }
    [MaxLength(50)] public string? PhotoContentType { get; set; }
    [NotMapped] public bool HasPhoto => !string.IsNullOrEmpty(PhotoFileName);
    public Position Position { get; set; } = null!;
}
