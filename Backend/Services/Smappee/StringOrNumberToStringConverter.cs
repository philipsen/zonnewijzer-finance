using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Services.Smappee;

public sealed class StringOrNumberToStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDouble().ToString("G", CultureInfo.InvariantCulture),
            _ => null
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
