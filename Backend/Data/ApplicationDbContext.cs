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
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<EventLog> EventLogs { get; set; }
        public DbSet<EnergyPrice> EnergyPrices { get; set; }
        public DbSet<ChargingUserProfile> ChargingUserProfiles { get; set; }
        public DbSet<MolliePayment> MolliePayments { get; set; }

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

            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.Year).IsRequired();
                entity.Property(e => e.Month).IsRequired();
                entity.Property(e => e.InvoiceNumber).HasMaxLength(255).IsRequired();
                entity.Property(e => e.TotalAmount).HasPrecision(10, 2).IsRequired();
                entity.Property(e => e.PdfPath).HasMaxLength(500);
                
                // Foreign key to User
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                
                // Unique constraint for user + year + month to prevent duplicate invoices
                entity.HasIndex(e => new { e.UserId, e.Year, e.Month }).IsUnique();
            });

            modelBuilder.Entity<EnergyPrice>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Year).IsRequired();
                entity.Property(e => e.Month).IsRequired();
                entity.Property(e => e.PricePerKwh).HasPrecision(8, 4).IsRequired();

                // Unique constraint: one price per year+month
                entity.HasIndex(e => new { e.Year, e.Month }).IsUnique();
            });

            modelBuilder.Entity<ChargingUserProfile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.DisplayName).HasMaxLength(255);
            });

            modelBuilder.Entity<MolliePayment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ChargingUserId).IsRequired();
                entity.Property(e => e.Year).IsRequired();
                entity.Property(e => e.Month).IsRequired();
                entity.Property(e => e.MolliePaymentId).HasMaxLength(100).IsRequired();
                entity.Property(e => e.CheckoutUrl).HasMaxLength(500).IsRequired();
                entity.Property(e => e.Amount).HasPrecision(10, 2).IsRequired();
                entity.Property(e => e.Status).HasMaxLength(50).IsRequired();

                // Unique constraint: one payment per charging user + year + month
                entity.HasIndex(e => new { e.ChargingUserId, e.Year, e.Month }).IsUnique();
            });
        }
    }
}