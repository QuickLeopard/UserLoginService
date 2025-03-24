using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace UserLoginService.Models
{
    [Table("user_login_records")]
    [Index(nameof(IpNumericHigh), nameof(IpNumericLow), Name = "IX_UserLoginRecord_IpNumeric")]
    [PrimaryKey(nameof(UserId), nameof(IpAddress))]
    public class UserLoginRecord
    {
        //[Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public long Id { get; set; }
       
        [Required]
        [Column("user_id")]
        public long UserId { get; set; }
       
        [Required]
        [Column("ip_address")]
        [MaxLength(45)] // To accommodate both IPv4 and IPv6 addresses
        public string IpAddress { get; set; } = string.Empty;

        [Required]
        [Column("login_timestamp")]
        public DateTime LoginTimestamp { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Add numeric IP address fields for faster pattern matching
        [Column("ip_numeric_high")]
        public Int64 IpNumericHigh { get; set; } // High 64 bits for IPv6, or 0 for IPv4
        
        [Column("ip_numeric_low")]
        public Int64 IpNumericLow { get; set; } // Low 64 bits for IPv6, or full IPv4 as uint32
    }
}
