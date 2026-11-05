using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class BankTransaction
{
    public const string StatusOpen = "Open";
    public const string StatusConfirmed = "Confirmed";
    public const string StatusIgnored = "Ignored";
    public const string DirectionCredit = "Credit";
    public const string DirectionDebit = "Debit";

    [Key]
    public int Id { get; set; }

    public DateOnly BookedOn { get; set; }

    [Required]
    [StringLength(34)]
    public string AccountIban { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    [Required]
    [StringLength(10)]
    public string Direction { get; set; } = DirectionCredit;

    [Required]
    [StringLength(200)]
    public string CounterpartyName { get; set; } = string.Empty;

    [StringLength(11)]
    public string? CounterpartyBic { get; set; }

    [StringLength(34)]
    public string? CounterpartyIban { get; set; }

    [StringLength(8)]
    public string? TransactionCode { get; set; }

    [Required]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    public decimal? BalanceAfter { get; set; }

    [Required]
    [StringLength(64)]
    public string Fingerprint { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string ReviewStatus { get; set; } = StatusOpen;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
