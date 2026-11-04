using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class EnergyPrice
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public int Month { get; set; }

        [Required]
        public decimal PricePerKwh { get; set; }

        /// <summary>Added to the Mollie checkout amount for this month. The invoice total stays unchanged.</summary>
        [Required]
        public decimal MollieSurchargeEur { get; set; } = 0.32m;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
