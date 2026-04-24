using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class MolliePayment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ChargeCardId { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public int Month { get; set; }

        [Required]
        [StringLength(100)]
        public string MolliePaymentId { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string CheckoutUrl { get; set; } = string.Empty;

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "open";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
