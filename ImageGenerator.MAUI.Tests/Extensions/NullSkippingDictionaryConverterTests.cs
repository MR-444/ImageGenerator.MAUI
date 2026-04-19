using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Models.Replicate;

namespace ImageGenerator.MAUI.Tests.Extensions;

public class NullSkippingDictionaryConverterTests
{
    private static readonly JsonSerializerOptions Options = RefitServiceExtensions.CreateContentSerializerOptions();

    [Fact]
    public void Serialize_DictionaryWithNullValue_OmitsKey()
    {
        var payload = new Dictionary<string, object?>
        {
            ["prompt"] = "hello",
            ["images"] = null
        };

        var json = JsonSerializer.Serialize(payload, Options);

        json.Should().NotContain("images");
        json.Should().Contain("\"prompt\":\"hello\"");
    }

    [Fact]
    public void Serialize_DictionaryWithNonNullValues_PreservesAllKeys()
    {
        var payload = new Dictionary<string, object?>
        {
            ["prompt"] = "hello",
            ["images"] = new[] { "data:image/png;base64,abc" },
            ["seed"] = 42L
        };

        var json = JsonSerializer.Serialize(payload, Options);

        json.Should().Contain("\"prompt\":\"hello\"");
        json.Should().Contain("\"images\":[\"data:image/png;base64,abc\"]");
        json.Should().Contain("\"seed\":42");
    }

    [Fact]
    public void RoundTrip_PreservesNonNullEntries()
    {
        var payload = new Dictionary<string, object?>
        {
            ["prompt"] = "hello",
            ["seed"] = 42L
        };

        var json = JsonSerializer.Serialize(payload, Options);
        var restored = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, Options);

        restored.Should().NotBeNull();
        restored!.Should().ContainKey("prompt");
        restored.Should().ContainKey("seed");
    }

    [Fact]
    public void Serialize_PolymorphicThroughReplicatePredictionRequest_OmitsNullDictEntries()
    {
        // Replicate's wrapper types `Input` as `object`. STJ dispatches by runtime type,
        // so this regression test catches any future mistyping of the payload dict.
        var request = new ReplicatePredictionRequest
        {
            Input = new Dictionary<string, object?>
            {
                ["prompt"] = "hello",
                ["images"] = null
            }
        };

        var json = JsonSerializer.Serialize(request, Options);

        json.Should().Contain("\"input\":");
        json.Should().NotContain("images");
        json.Should().Contain("\"prompt\":\"hello\"");
    }
}
