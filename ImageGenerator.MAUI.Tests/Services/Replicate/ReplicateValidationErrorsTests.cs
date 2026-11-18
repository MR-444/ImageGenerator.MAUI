using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;

namespace ImageGenerator.MAUI.Tests.Services.Replicate;

/// <summary>
/// The 422 body is the only place Replicate writes down WHY it rejected a payload. These tests pin
/// that the summary names the field, keeps the accepted values, and above all never swallows a
/// body the parser doesn't recognise — those return false and keep the caller's raw dump.
/// </summary>
public class ReplicateValidationErrorsTests
{
    /// <summary>Captured from the app log for openai/gpt-image-2.6-sunburst on 2026-11-17 — the
    /// rejected request this whole Phase 3 thread started from.</summary>
    private const string SunburstBody =
        """
        {"detail":"- input.aspect_ratio: aspect_ratio must be one of the following: \"1:1\", \"3:2\", \"2:3\", \"16:9\", \"9:16\", \"auto\", \"1024x1024\"\n","status":422,"title":"Input validation failed","invalid_fields":[{"type":"enum","field":"input.aspect_ratio","description":"aspect_ratio must be one of the following: \"1:1\", \"3:2\", \"2:3\", \"16:9\", \"9:16\", \"auto\", \"1024x1024\""}]}
        """;

    [Fact]
    public void TrySummarize_RealSunburst422_NamesTheFieldAndKeepsTheAcceptedValues()
    {
        ReplicateValidationErrors.TrySummarize(SunburstBody, out var message).Should().BeTrue();

        message.Should().NotBeNull();
        message.Should().StartWith("Input validation failed:");
        // The `input.` prefix is Replicate's internal path; the app never shows it.
        message.Should().Contain("aspect_ratio —").And.NotContain("input.aspect_ratio");
        // The accepted values are the actionable half of the message — without them the user
        // screenshots the error instead of fixing it.
        message.Should().Contain("must be one of the following").And.Contain("1024x1024");
        // And it reads as a sentence, not as JSON.
        message.Should().NotContain("invalid_fields").And.NotContain("{");
    }

    [Fact]
    public void TrySummarize_ValidationListShape_NamesTheFieldFromLoc()
    {
        const string body =
            """
            {"detail":[{"loc":["body","input","aspect_ratio"],"msg":"value is not a valid enumeration member; permitted: \"1:1\", \"3:2\"","type":"value_error.enum"}],"title":"Validation Error","status":422}
            """;

        ReplicateValidationErrors.TrySummarize(body, out var message).Should().BeTrue();

        message.Should().Be(
            @"Validation Error: aspect_ratio — value is not a valid enumeration member; permitted: ""1:1"", ""3:2""");
    }

    [Fact]
    public void TrySummarize_FieldWithoutDescription_FallsBackToTheDetailSentence()
    {
        // The degenerate proxy shape: the structured part names the field but the accepted values
        // live only in `detail`. Dropping them would make the message worse than the raw body.
        const string body =
            """
            {"detail":"- input.aspect_ratio: aspect_ratio must be one of \"1:1\", \"3:2\"","status":422,"title":"Input validation failed","invalid_fields":[{"type":"enum","field":"input.aspect_ratio"}]}
            """;

        ReplicateValidationErrors.TrySummarize(body, out var message).Should().BeTrue();

        // The field's own name is not repeated back twice.
        message.Should().Be(@"Input validation failed: aspect_ratio — must be one of ""1:1"", ""3:2""");
    }

    [Fact]
    public void TrySummarize_MultipleInvalidFields_ListsEveryOne()
    {
        const string body =
            """
            {"status":422,"title":"Input validation failed","invalid_fields":[{"type":"enum","field":"input.aspect_ratio","description":"bad ratio"},{"type":"integer","field":"input.seed","description":"ensure this value is greater than 0"}]}
            """;

        ReplicateValidationErrors.TrySummarize(body, out var message).Should().BeTrue();

        message.Should().Be("Input validation failed: aspect_ratio — bad ratio; seed — ensure this value is greater than 0");
    }

    [Fact]
    public void TrySummarize_AbsurdlyLongBody_TruncatesButStillNamesTheField()
    {
        // 200 allowed values is a real reply from a real model; the label is two lines long.
        var values = string.Join(", ", Enumerable.Range(0, 200).Select(i => $"\\\"{i}x{i}\\\""));
        var body =
            "{\"status\":422,\"title\":\"Input validation failed\",\"invalid_fields\":[{"
            + "\"type\":\"enum\",\"field\":\"input.aspect_ratio\","
            + $"\"description\":\"aspect_ratio must be one of the following: {values}\""
            + "}]}";

        ReplicateValidationErrors.TrySummarize(body, out var message).Should().BeTrue();

        // Bounded and marked as cut; the caller logs the full body. Truncation happens at the tail,
        // so the field name always survives it.
        message.Should().EndWith("…");
        message!.Length.Should().BeLessThan(600, "a status label is one or two lines");
        message.Should().Contain("aspect_ratio");
        // And the cut lands on a value boundary, not inside one: an enum list chopped to "0x" tells
        // the user nothing, so the ellipsis has to follow a completed quoted value.
        message[^2].Should().Be('"');
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain text failure")]
    [InlineData("<html>502 Bad Gateway</html>")]
    // JSON, but with nothing this class could add over the raw body.
    [InlineData("{}")]
    [InlineData("{\"status\":422,\"title\":\"Input validation failed\"}")]
    [InlineData("{\"detail\":\"Input validation failed\",\"status\":422}")]
    // JSON that starts like an object and isn't one.
    [InlineData("{\"detail\":")]
    public void TrySummarize_EverythingElse_ReportsNoSummary(string? body)
    {
        // False is the contract that keeps the raw dump alive: a body this class can't read must
        // never be replaced by a confident nothing.
        ReplicateValidationErrors.TrySummarize(body, out var message).Should().BeFalse();
        message.Should().BeNull();
    }
}
