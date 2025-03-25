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

            modelBuilder.Entity<UserLoginRecord>().ToTable("user_login_records");

            // Define composite primary key
            modelBuilder.Entity<UserLoginRecord>()
                .HasKey(r => new { r.UserId, r.IpAddress });

            // Composite index for UserId + IpAddress
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => new { r.UserId, r.IpAddress })
                .HasDatabaseName("idx_user_login_records_user_id_ip_address");

            // Index on IpAddress for queries filtering only by IP
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => r.IpAddress)
                .HasDatabaseName("idx_user_login_records_ip_address");

            // Indexes for numeric IP parts (if used in range queries)
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => new { r.IpNumericHigh, r.IpNumericLow })
                .HasDatabaseName("idx_user_login_records_ip_numeric_range");

            // Composite index for UserId and numeric IP parts
            modelBuilder.Entity<UserLoginRecord>()
                .HasIndex(r => new { r.UserId, r.IpNumericHigh, r.IpNumericLow })
                .HasDatabaseName("idx_user_login_records_user_id_ip_numeric");
        }
    }
}
