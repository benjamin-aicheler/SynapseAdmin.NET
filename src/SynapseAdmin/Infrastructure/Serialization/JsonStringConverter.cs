using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynapseAdmin.Infrastructure.Serialization;

/// <summary>
/// A custom converter that allows reading JSON Numbers (and other types) as Strings.
/// This is used as a workaround for Synapse Admin API inconsistencies where 
/// next_token can be either a string or a number.
/// </summary>
public class JsonStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out long l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} when parsing string.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
