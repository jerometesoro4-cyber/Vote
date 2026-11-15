using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace voting.Models
{
    public class Vote
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("vote_id")]
        public int vote_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("student_id")]
        public int student_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("candidate_id")]
        public int candidate_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("tally_id")]
        public int tally_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("vote_timestamp")]
        public string vote_timestamp { get; set; } = string.Empty;
    }
}