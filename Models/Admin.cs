using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace voting.Models
{
    public class Admin
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("admin_id")]
        public int admin_id { get; set; }

        [Required]
        [StringLength(100)]
        [Column("admin_Name")]
        public string admin_name { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("admin_Email")]
        public string admin_email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Column("password")]
        public string password { get; set; } = string.Empty;

        [Required]
        [Column("created_at")]
        public DateTime created_at { get; set; }
    }
}