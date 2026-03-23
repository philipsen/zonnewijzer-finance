using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class Invoice
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        public int Year { get; set; }
        
        [Required]
        public int Month { get; set; }
        
        [Required]
        [StringLength(255)]
        public string InvoiceNumber { get; set; } = string.Empty;
        
        [Required]
        [StringLength(255)]
        public string UserName { get; set; } = string.Empty;
        
        [Required]
        public decimal TotalAmount { get; set; }
        
        public string? PdfPath { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        
        // Navigation properties
        public User User { get; set; } = null!;
    }
}