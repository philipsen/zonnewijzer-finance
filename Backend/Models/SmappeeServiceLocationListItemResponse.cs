using System.Text.Json.Serialization;

namespace Backend.Models;

/// <summary>Row from Smappee GET <c>servicelocation</c> for admin UI, optionally enriched from stored <c>…/info</c>.</summary>
public class SmappeeServiceLocationListItemResponse
{
    public int ServiceLocationId { get; set; }
    public string? Name { get; set; }
    public string? ServiceLocationUuid { get; set; }
    public string? DeviceSerialNumber { get; set; }

    public double? Longitude { get; set; }
    public double? Latitude { get; set; }
    public decimal? ElectricityCost { get; set; }
    public string? ElectricityCurrency { get; set; }
    public string? Timezone { get; set; }
    public IReadOnlyList<SmappeeServiceLocationApplianceItemResponse>? Appliances { get; set; }
}

public class SmappeeServiceLocationApplianceItemResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }
}
