using Microsoft.EntityFrameworkCore;
using Backend.Models;

namespace Backend.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<ChargingSession> ChargingSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GoogleId).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.GoogleId).IsRequired();
                entity.Property(e => e.Email).IsRequired();
                entity.Property(e => e.Name).IsRequired();
            });

            modelBuilder.Entity<ChargingSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Start).IsRequired();
                entity.Property(e => e.End).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.StationId).IsRequired();
                entity.Property(e => e.ChargedKwh).HasPrecision(8, 2).IsRequired();
                
                // Create unique index to prevent duplicates
                entity.HasIndex(e => new { e.Start, e.UserId, e.StationId }).IsUnique();
            });
        }
    }
}