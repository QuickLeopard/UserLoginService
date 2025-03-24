using Microsoft.EntityFrameworkCore;
using UserLoginService.Models;

namespace UserLoginService.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UserLoginRecord> UserLoginRecords { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //modelBuilder.Entity<UserLoginRecord>()
            //.HasKey(nameof(UserLoginRecord.UserId), nameof((UserLoginRecord.IpAddress)));

            // Create index on UserId for faster lookups
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => r.UserId)
                .HasDatabaseName("idx_user_login_records_user_id");

            // Create index on IpAddress for faster lookups
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => r.IpAddress)
                .HasDatabaseName("idx_user_login_records_ip_address");

            // Create a compound index on both UserId and IpAddress
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => new { r.UserId, r.IpAddress })
                .HasDatabaseName("idx_user_login_records_user_id_ip_address");

            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => r.IpNumericHigh)
                .HasDatabaseName("idx_user_login_records_ip_numeric_high");

            modelBuilder.Entity<UserLoginRecord>()
               .HasIndex(r => r.IpNumericLow)
               .HasDatabaseName("idx_user_login_records_ip_numeric_low");

            modelBuilder.Entity<UserLoginRecord>()
               .HasIndex(r => new { r.UserId, r.IpNumericHigh, r.IpNumericLow })
               .HasDatabaseName("idx_user_login_records_user_id_ip_numeric_high_ip_numeric_low");
        }
    }
}
