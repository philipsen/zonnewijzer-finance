namespace Backend.Models;

/// <summary>Many-to-many: an app user may have several charge card (RFID) tokens; each card links to at most one user.</summary>
public class UserChargeCard
{
    public int UserId { get; set; }
    public int ChargeCardId { get; set; }

    public User? User { get; set; }
    public ChargeCard? ChargeCard { get; set; }
}
