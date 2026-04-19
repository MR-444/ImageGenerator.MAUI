using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Infrastructure.External.Replicate;

/// <summary>
/// Replicate's "output" field is either a single URL string (some older Flux Pro variants)
/// or an array of URL strings. Normalize to a list so callers can always do output?.FirstOrDefault().
/// </summary>
public sealed class ReplicateOutputConverter : JsonConverter<IReadOnlyList<string>?>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return [reader.GetString()!];

            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        list.Add(reader.GetString()!);
                    }
                }
                return list;

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for Replicate output.");
        }
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string>? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        foreach (var url in value) writer.WriteStringValue(url);
        writer.WriteEndArray();
    }
}
