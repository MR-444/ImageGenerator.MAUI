using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram;

/// <summary>Thrown when a string cannot be parsed as a structured V4 prompt.</summary>
public sealed class V4JsonPromptParseException : Exception
{
    public V4JsonPromptParseException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Single serialization authority for <see cref="V4JsonPrompt"/>. Compact output (the default)
/// must byte-match Python's <c>json.dumps(separators=(",",":"))</c> — System.Text.Json with
/// <c>WriteIndented=false</c> already emits no whitespace, and a unit test pins the exact
/// string so nobody hand-rolls a minifier later. Indented output is for the on-disk export.
/// </summary>
public static class V4JsonPromptSerializer
{
    // UnsafeRelaxedJsonEscaping keeps non-ASCII (umlauts) and quotes-only escaping in the
    // exported file readable; the payload stays valid JSON either way because json_prompt is
    // re-escaped as a plain string value when the outer Replicate payload is serialized.
    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new ElementJsonConverter() }
    };

    private static readonly JsonSerializerOptions Indented = new(Compact)
    {
        WriteIndented = true
    };

    public static string Serialize(V4JsonPrompt model, bool indented = false) =>
        JsonSerializer.Serialize(model, indented ? Indented : Compact);

    public static V4JsonPrompt Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new V4JsonPromptParseException("Input is empty.");

        try
        {
            return JsonSerializer.Deserialize<V4JsonPrompt>(json, Compact)
                   ?? throw new V4JsonPromptParseException("Input deserialized to null (JSON 'null').");
        }
        catch (JsonException ex)
        {
            throw new V4JsonPromptParseException($"Not a valid structured prompt: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes elements with schema-exact key order (type, bbox, text, desc, color_palette),
    /// omits keys that don't apply (obj never gets <c>text</c>, empty palettes vanish), and
    /// reads leniently — unknown keys are skipped so hand-authored JSON round-trips.
    /// </summary>
    private sealed class ElementJsonConverter : JsonConverter<Element>
    {
        public override void Write(Utf8JsonWriter writer, Element value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Type);

            if (value.Bbox is not null)
            {
                writer.WriteStartArray("bbox");
                foreach (var coordinate in value.Bbox) writer.WriteNumberValue(coordinate);
                writer.WriteEndArray();
            }

            if (value.Type == Element.TextType && value.Text is not null)
                writer.WriteString("text", value.Text);

            if (value.Desc is not null)
                writer.WriteString("desc", value.Desc);

            if (value.ColorPalette is { Count: > 0 })
            {
                writer.WriteStartArray("color_palette");
                foreach (var color in value.ColorPalette) writer.WriteStringValue(color);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        public override Element Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Element must be a JSON object.");

            var element = new Element { Type = string.Empty };

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "type":
                        element.Type = reader.GetString() ?? string.Empty;
                        break;
                    case "bbox":
                        element.Bbox = JsonSerializer.Deserialize<int[]>(ref reader, options);
                        break;
                    case "text":
                        element.Text = reader.GetString();
                        break;
                    case "desc":
                        element.Desc = reader.GetString();
                        break;
                    case "color_palette":
                        element.ColorPalette = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (element.Type is not (Element.ObjType or Element.TextType))
                throw new JsonException($"Element type must be '{Element.ObjType}' or '{Element.TextType}', got '{element.Type}'.");

            return element;
        }
    }
}
