using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Application.Services;

namespace ImageGenerator.MAUI.Tests.Services;

public class PromptBatchParserTests
{
    private readonly PromptBatchParser _parser = new();

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        _parser.Parse(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyList()
    {
        _parser.Parse("   \n\n  \r\n  ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_SinglePrompt_NoDelimiter_ReturnsOne()
    {
        var result = _parser.Parse("A young woman holding a bouquet");
        result.Should().ContainSingle().Which.Should().Be("A young woman holding a bouquet");
    }

    [Fact]
    public void Parse_TwoPrompts_SplitByDashDashDash_ReturnsBoth()
    {
        const string input = """
            A young woman holding a bouquet
            ---
            Mountain range at golden hour
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("A young woman holding a bouquet");
        result[1].Should().Be("Mountain range at golden hour");
    }

    [Fact]
    public void Parse_MultilinePrompts_PreservesInternalNewlines()
    {
        const string input = """
            A young woman holding a bouquet,
            standing in a sunlit meadow,
            cinematic lighting
            ---
            Mountain range at golden hour,
            ultra-wide cinematic
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("A young woman holding a bouquet,\nstanding in a sunlit meadow,\ncinematic lighting");
        result[1].Should().Be("Mountain range at golden hour,\nultra-wide cinematic");
    }

    [Fact]
    public void Parse_BlankLineSeparatesPrompts()
    {
        const string input = """
            A young woman holding a bouquet

            Mountain range at golden hour
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("A young woman holding a bouquet");
        result[1].Should().Be("Mountain range at golden hour");
    }

    [Fact]
    public void Parse_MultipleBlankLines_CollapseToOneBoundary()
    {
        // A run of blank lines is a single separator — no empty prompts emitted.
        const string input = "Prompt A\n\n\n\nPrompt B";

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result.Should().ContainInOrder("Prompt A", "Prompt B");
    }

    [Fact]
    public void Parse_CommentLineSeparatesPrompts()
    {
        // A # comment line ends the current prompt even with no blank line around it.
        const string input = """
            Prompt A
            # header between prompts
            Prompt B
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("Prompt A");
        result[1].Should().Be("Prompt B");
    }

    [Fact]
    public void Parse_CommentBlockBetweenPrompts_RealWorldFormat()
    {
        // Mirrors the user's file: a # header block plus blank lines wrapping single-line
        // prompts, with no --- delimiters anywhere.
        const string input = """
            # ============================
            # FRAME 01 — INTRO
            # ============================

            Oil on dark-toned canvas, frame one.

            # ============================
            # FRAME 02 — VERSE 1
            # ============================

            Oil on dark-toned canvas, frame two.
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("Oil on dark-toned canvas, frame one.");
        result[1].Should().Be("Oil on dark-toned canvas, frame two.");
        result.Should().NotContain(p => p.Contains('#'));
    }

    [Fact]
    public void Parse_BlankLineInsidePrompt_NowSplits()
    {
        // Accepted trade-off: a blank line inside a prompt splits it. Use --- to keep a
        // blank line within a single multi-paragraph prompt instead.
        const string input = "first paragraph\n\nsecond paragraph";

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result.Should().ContainInOrder("first paragraph", "second paragraph");
    }

    [Fact]
    public void Parse_CommentLines_AreDropped()
    {
        const string input = """
            # this is a comment
            Real prompt one
            ---
            # disabled — skip me entirely
            Real prompt two
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("Real prompt one");
        result[1].Should().Be("Real prompt two");
    }

    [Fact]
    public void Parse_DelimiterWithSurroundingWhitespace_StillRecognized()
    {
        // "---" with leading/trailing spaces or tabs on the line still counts as a separator.
        var input = "Prompt A\n   ---   \nPrompt B\n\t---\t\nPrompt C";

        var result = _parser.Parse(input);

        result.Should().HaveCount(3);
        result.Should().ContainInOrder("Prompt A", "Prompt B", "Prompt C");
    }

    [Fact]
    public void Parse_DashDashDashInsideText_IsNotSplit()
    {
        // A line containing "---" alongside other text is NOT a delimiter — only a line whose
        // trimmed content equals exactly "---".
        const string input = "A prompt with --- inline dashes\n---\nNext prompt";

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("A prompt with --- inline dashes");
        result[1].Should().Be("Next prompt");
    }

    [Fact]
    public void Parse_EmptyChunksBetweenDelimiters_AreDropped()
    {
        const string input = """
            Prompt A
            ---
            ---


            ---
            Prompt B
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("Prompt A");
        result[1].Should().Be("Prompt B");
    }

    [Fact]
    public void Parse_TrailingDelimiter_DoesNotCreateEmptyTrailingPrompt()
    {
        const string input = """
            Prompt A
            ---
            Prompt B
            ---
            """;

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_BomPrefixedInput_BomIsStripped()
    {
        // Build the BOM via char code so this test source survives editor/transport
        // normalization that might strip an inline U+FEFF.
        var input = ((char)0xFEFF) + "Prompt A\n---\nPrompt B";

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("Prompt A");
        result[1].Should().Be("Prompt B");
    }

    [Fact]
    public void Parse_CrlfLineEndings_HandledIdenticallyToLf()
    {
        var input = "Prompt A\r\n---\r\nPrompt B\r\nsecond line";

        var result = _parser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Should().Be("Prompt A");
        result[1].Should().Be("Prompt B\nsecond line");
    }

    [Fact]
    public void Parse_OverHardCap_Throws()
    {
        // 101 prompts → throw.
        var input = string.Join("\n---\n", Enumerable.Range(0, 101).Select(i => $"prompt {i}"));

        var act = () => _parser.Parse(input);

        act.Should().Throw<PromptBatchTooLargeException>()
           .Which.PromptCount.Should().Be(101);
    }

    [Fact]
    public void Parse_AtHardCap_Succeeds()
    {
        var input = string.Join("\n---\n", Enumerable.Range(0, PromptBatchParserLimits.MaxPromptsPerBatch).Select(i => $"prompt {i}"));

        var result = _parser.Parse(input);

        result.Should().HaveCount(PromptBatchParserLimits.MaxPromptsPerBatch);
    }
}
