using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace voting.Models
{
    public class Voter
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("student_id")]
        public int student_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("student_name")]
        public string student_name { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("student_email")]
        public string student_email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("student_password")]
        public string student_password { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("year_level")]
        public string year_level { get; set; } = string.Empty;
    }
}