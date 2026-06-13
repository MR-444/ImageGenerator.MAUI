using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Services;

namespace ImageGenerator.MAUI.Tests.Services.Civitai;

public sealed class CivitaiTitleBuilderTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("   \t\n ", "")]
    [InlineData("a red fox", "a red fox")]
    [InlineData("a   red\n\nfox", "a red fox")]
    public void Build_CollapsesWhitespace_EmptyMeansNoTitle(string prompt, string expected)
    {
        CivitaiTitleBuilder.Build(prompt).Should().Be(expected);
    }

    [Fact]
    public void Build_JsonPrompt_UsesHighLevelDescription()
    {
        const string json = """{"high_level_description":"A dark-haired woman at dusk","style":"photo"}""";

        CivitaiTitleBuilder.Build(json).Should().Be("A dark-haired woman at dusk");
    }

    [Fact]
    public void Build_JsonPrompt_FallsBackThroughKnownKeys()
    {
        CivitaiTitleBuilder.Build("""{"style":"photo","caption":"A red fox"}""")
            .Should().Be("A red fox");
    }

    [Fact]
    public void Build_JsonPromptWithoutUsableField_ReturnsEmpty()
    {
        CivitaiTitleBuilder.Build("""{"style":"photo","steps":30}""")
            .Should().BeEmpty("no title beats a raw JSON blob as the post title");
    }

    [Fact]
    public void Build_MalformedJson_ReturnsEmpty()
    {
        CivitaiTitleBuilder.Build("{\"broken\": ").Should().BeEmpty();
    }

    [Fact]
    public void Build_LongPrompt_CutsAtWordBoundaryWithEllipsis()
    {
        var prompt = "A cinematic in-camera double-exposure photograph at eye level of a young woman";

        var title = CivitaiTitleBuilder.Build(prompt);

        title.Length.Should().BeLessThanOrEqualTo(61);
        title.Should().EndWith("…");
        title.Should().NotContain("  ");
        prompt.Should().StartWith(title[..^1], "the cut must happen at a word boundary, not mid-word");
    }
}
