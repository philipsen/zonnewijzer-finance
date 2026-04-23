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

    [JsonPropertyName("authenticationType")]
    public string? AuthenticationType { get; set; }

    [JsonPropertyName("rfid")]
    public string? Rfid { get; set; }

    [JsonPropertyName("csmsTransactionId")]
    public string? CsmsTransactionId { get; set; }

    [JsonPropertyName("from")]
    public long From { get; set; }

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

    public string SerialNumberNormalized() => (SerialNumber ?? "").Trim();
}
