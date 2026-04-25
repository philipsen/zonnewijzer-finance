namespace Backend.Models;

public class ChargeCard
{
    public int Id { get; set; }

    /// <summary>Smappee whitelist id; null for cards created only from session RFIDs.</summary>
    public int? SmappeeWhitelistId { get; set; }

    /// <summary>RFID value (Smappee <c>value</c>).</summary>
    public string Value { get; set; } = "";

    /// <summary>Smappee <c>comment</c> (display name).</summary>
    public string? Comment { get; set; }

    /// <summary>Epoch milliseconds; Smappee <c>expirationTimestamp</c>.</summary>
    public long? ExpirationTimestamp { get; set; }

    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? PlaceName { get; set; }

    public ICollection<UserChargeCard> UserChargeCards { get; set; } = new List<UserChargeCard>();
}
