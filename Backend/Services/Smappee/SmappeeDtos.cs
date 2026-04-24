using System.Text.Json.Serialization;

namespace Backend.Services.Smappee;

public class SmappeeTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

public class SmappeeRfidDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("expirationTimestamp")]
    public long? ExpirationTimestamp { get; set; }
}

public class SmappeeSessionDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("serialNumber")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? SerialNumber { get; set; }

    /// <summary>Connector index from Smappee (per session); combined with <see cref="SerialNumber"/> for charging point id.</summary>
    [JsonPropertyName("connector")]
    public int? Connector { get; set; }

    [JsonPropertyName("authenticationType")]
    public string? AuthenticationType { get; set; }

    [JsonPropertyName("rfid")]
    public string? Rfid { get; set; }

    [JsonPropertyName("csmsTransactionId")]
    public string? CsmsTransactionId { get; set; }

    /// <summary>Start time; API sends Unix <strong>seconds</strong> (normalized to ms in <see cref="SmappeeSessionTimestamps"/>).</summary>
    [JsonPropertyName("from")]
    public long From { get; set; }

    /// <summary>End time if finished; omitted/null while session is ongoing.</summary>
    [JsonPropertyName("to")]
    public long? To { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("range")]
    public int? Range { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("startReading")]
    public decimal? StartReading { get; set; }

    [JsonPropertyName("stopReading")]
    public decimal? StopReading { get; set; }

    /// <summary>Delivered energy in kWh (Smappee v3); should match <c>stopReading - startReading</c> when start is zero.</summary>
    [JsonPropertyName("energy")]
    public decimal? Energy { get; set; }

    public string SerialNumberNormalized() => (SerialNumber ?? "").Trim();
}

/// <summary>GET <c>servicelocation</c> — see Smappee "Get Servicelocations" (v3).</summary>
public class SmappeeServiceLocationsListResponse
{
    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    [JsonPropertyName("serviceLocations")]
    public List<SmappeeServiceLocationRowDto>? ServiceLocations { get; set; }
}

public class SmappeeServiceLocationRowDto
{
    [JsonPropertyName("serviceLocationId")]
    public int ServiceLocationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("serviceLocationUuid")]
    public string? ServiceLocationUuid { get; set; }

    [JsonPropertyName("deviceSerialNumber")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? DeviceSerialNumber { get; set; }
}

/// <summary>GET <c>servicelocation/{{id}}/info</c> (v3).</summary>
public class SmappeeServiceLocationInfoDto
{
    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("electricityCost")]
    public double ElectricityCost { get; set; }

    [JsonPropertyName("electricityCurrency")]
    public string? ElectricityCurrency { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("appliances")]
    public List<SmappeeServiceLocationApplianceDto>? Appliances { get; set; }
}

public class SmappeeServiceLocationApplianceDto
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
