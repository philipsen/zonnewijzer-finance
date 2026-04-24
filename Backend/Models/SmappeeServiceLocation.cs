namespace Backend.Models;

/// <summary>Park-level snapshot from Smappee GET <c>servicelocation/{{id}}/info</c>.</summary>
public class SmappeeServiceLocation
{
    public int ServiceLocationId { get; set; }

    public double? Longitude { get; set; }
    public double? Latitude { get; set; }

    public decimal? ElectricityCost { get; set; }
    public string? ElectricityCurrency { get; set; }

    public string? Timezone { get; set; }

    /// <summary>JSON array of appliances (see Smappee docs).</summary>
    public string? AppliancesJson { get; set; }

    public DateTime? InfoFetchedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
