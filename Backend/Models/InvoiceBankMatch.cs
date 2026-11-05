using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class InvoiceBankMatch
{
    public const string KindCertain = "Certain";
    public const string KindSuggested = "Suggested";
    public const string KindManual = "Manual";

    [Key]
    public int Id { get; set; }

    public int BankTransactionId { get; set; }

    public BankTransaction BankTransaction { get; set; } = null!;

    public int AppUserId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    [Required]
    [StringLength(255)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string MatchKind { get; set; } = KindManual;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
