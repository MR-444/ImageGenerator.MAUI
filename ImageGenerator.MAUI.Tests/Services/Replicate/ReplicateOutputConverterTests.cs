using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using System.Text.Json;

namespace ImageGenerator.MAUI.Tests.Services.Replicate;

public class ReplicateOutputConverterTests
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions();
        o.Converters.Add(new ReplicateOutputConverter());
        return o;
    }

    [Fact]
    public void Read_NullToken_ReturnsNull()
    {
        var result = JsonSerializer.Deserialize<IReadOnlyList<string>?>("null", Options);

        result.Should().BeNull();
    }

    [Fact]
    public void Read_SingleString_ReturnsListOfOne()
    {
        var result = JsonSerializer.Deserialize<IReadOnlyList<string>?>("\"https://example.com/a.png\"", Options);

        result.Should().BeEquivalentTo(new[] { "https://example.com/a.png" });
    }

    [Fact]
    public void Read_ArrayOfStrings_ReturnsList()
    {
        var result = JsonSerializer.Deserialize<IReadOnlyList<string>?>("[\"a\",\"b\",\"c\"]", Options);

        result.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public void Read_EmptyArray_ReturnsEmptyList()
    {
        var result = JsonSerializer.Deserialize<IReadOnlyList<string>?>("[]", Options);

        result.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Read_NumberToken_Throws()
    {
        var act = () => JsonSerializer.Deserialize<IReadOnlyList<string>?>("42", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_ObjectToken_Throws()
    {
        var act = () => JsonSerializer.Deserialize<IReadOnlyList<string>?>("{\"k\":\"v\"}", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_ArrayContainingNull_PinsCurrentSilentDropBehavior()
    {
        // Pinned per audit M7 — current converter drops non-string array items silently.
        // If M7 is fixed (throw on null), update this test to assert JsonException.
        var result = JsonSerializer.Deserialize<IReadOnlyList<string>?>("[\"a\",null,\"b\"]", Options);

        result.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void Write_NullValue_EmitsJsonNull()
    {
        var json = JsonSerializer.Serialize<IReadOnlyList<string>?>(null, Options);

        json.Should().Be("null");
    }

    [Fact]
    public void Write_List_EmitsJsonArray()
    {
        var json = JsonSerializer.Serialize<IReadOnlyList<string>?>(new[] { "a", "b" }, Options);

        json.Should().Be("[\"a\",\"b\"]");
    }
}
