using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    /// <summary>
    /// An extra line on one user's invoice. The calendar month of <see cref="Date"/> selects the invoice.
    /// The amount is VAT-inclusive and can be negative.
    /// </summary>
    public class InvoiceCorrection
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateOnly Date { get; set; }

        [Required]
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public decimal Amount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public User User { get; set; } = null!;
    }
}
