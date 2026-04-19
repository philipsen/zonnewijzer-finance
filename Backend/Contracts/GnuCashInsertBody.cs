namespace Backend.Contracts;

public sealed class GnuCashInsertBody
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? ChargingUserId { get; set; }
}
