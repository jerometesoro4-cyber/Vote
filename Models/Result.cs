using System;
using System.ComponentModel.DataAnnotations;    
using System.ComponentModel.DataAnnotations.Schema;

namespace voting.Models
{
    public class Result
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("tally_id")]
        public int tally_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("candidate_id")]
        public int candidate_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("total_votes")]
        public int total_votes { get; set; }

        [Required]
        [StringLength(100)]
        [Column("candidate_position")]
        public string candidate_position { get; set; } = string.Empty;

    }
}