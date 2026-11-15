using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace voting.Models
{
    public class Candidate
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("candidate_id")]
        public int candidate_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("candidate_position")]
        public string candidate_position { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("party_name")]
        public string party_name { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("year_level")]
        public string year_level { get; set; } = string.Empty;
    }
}