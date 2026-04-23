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
        public DbSet<ChargeCard> ChargeCards { get; set; }
        public DbSet<MolliePayment> MolliePayments { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.Email).IsRequired();
                entity.Property(e => e.Name).IsRequired();
            });

            modelBuilder.Entity<ChargeCard>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Value).HasMaxLength(256).IsRequired();
                entity.Property(e => e.Comment).HasMaxLength(512);
                entity.HasIndex(e => e.Value).IsUnique();
                entity.HasIndex(e => e.SmappeeWhitelistId).IsUnique();
            });

            modelBuilder.Entity<ChargingSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.SerialNumber).HasMaxLength(64);
                entity.Property(e => e.AuthenticationType).HasMaxLength(64);
                entity.Property(e => e.Rfid).HasMaxLength(256);
                entity.Property(e => e.CsmsTransactionId).HasMaxLength(128);
                entity.Property(e => e.Status).HasMaxLength(32);
                entity.Property(e => e.StartReading).HasPrecision(18, 6);
                entity.Property(e => e.StopReading).HasPrecision(18, 6);
                entity.Property(e => e.ChargeCardId).IsRequired();
                entity.Property(e => e.StationId).IsRequired();
                entity.HasIndex(e => e.SmappeeSessionId).IsUnique();

                entity.HasOne<ChargeCard>()
                    .WithMany()
                    .HasForeignKey(e => e.ChargeCardId)
                    .OnDelete(DeleteBehavior.Restrict);
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

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.UserId, e.Year, e.Month }).IsUnique();
            });

            modelBuilder.Entity<EnergyPrice>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Year).IsRequired();
                entity.Property(e => e.Month).IsRequired();
                entity.Property(e => e.PricePerKwh).HasPrecision(8, 4).IsRequired();
                entity.HasIndex(e => new { e.Year, e.Month }).IsUnique();
            });

            modelBuilder.Entity<MolliePayment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ChargeCardId).IsRequired();
                entity.Property(e => e.Year).IsRequired();
                entity.Property(e => e.Month).IsRequired();
                entity.Property(e => e.MolliePaymentId).HasMaxLength(100).IsRequired();
                entity.Property(e => e.CheckoutUrl).HasMaxLength(500).IsRequired();
                entity.Property(e => e.Amount).HasPrecision(10, 2).IsRequired();
                entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
                entity.HasIndex(e => new { e.ChargeCardId, e.Year, e.Month }).IsUnique();
            });

            modelBuilder.Entity<PasswordResetToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.HasIndex(e => new { e.UserId, e.ExpiresAt });

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
