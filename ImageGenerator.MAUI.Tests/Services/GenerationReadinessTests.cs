using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Services;

public class GenerationReadinessTests
{
    private const string ComfyUiModel = ModelConstants.ComfyUi.PrefixSlash + "sdXL-workflow.json";

    private static ImageGenerationParameters Params(string model, string apiToken = "", string prompt = "a cat") =>
        new() { Model = model, ApiToken = apiToken, Prompt = prompt };

    [Theory]
    [InlineData(ModelConstants.Pollinations.Flux, true)]
    [InlineData(ComfyUiModel, true)]
    [InlineData(ModelConstants.Flux.Pro11, false)]
    [InlineData(null, false)]
    public void IsTokenless_OnlyForPollinationsAndComfyUi(string? model, bool expected) =>
        GenerationReadiness.IsTokenless(model).Should().Be(expected);

    [Fact]
    public void MissingFields_ReplicateWithoutTokenOrPrompt_NamesBothInDisplayOrder() =>
        GenerationReadiness.MissingFields(Params(ModelConstants.Flux.Pro11, apiToken: " ", prompt: ""))
            .Should().Equal("API Token", "Prompt");

    [Theory]
    [InlineData(ModelConstants.Pollinations.Flux)]
    [InlineData(ComfyUiModel)]
    public void MissingFields_TokenlessModel_NeedsOnlyThePrompt(string model)
    {
        GenerationReadiness.MissingFields(Params(model)).Should().BeEmpty();
        GenerationReadiness.MissingFields(Params(model, prompt: "  ")).Should().Equal("Prompt");
    }

    [Fact]
    public void HasRequiredToken_ReplicateWithToken_IsTrue() =>
        GenerationReadiness.HasRequiredToken(Params(ModelConstants.Flux.Pro11, apiToken: "r8_x"))
            .Should().BeTrue();

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void IsJsonModeActive_NeedsTheToggleAndAModelWithAJsonField(
        bool useJsonPrompt, bool modelHasJsonPrompt, bool expected)
    {
        var parameters = Params(ModelConstants.Flux.Pro11);
        parameters.UseJsonPrompt = useJsonPrompt;

        GenerationReadiness.IsJsonModeActive(parameters, modelHasJsonPrompt).Should().Be(expected);
    }

    [Fact]
    public void JsonErrorDetail_ValidJson_IsNull() =>
        GenerationReadiness.JsonErrorDetail("""{"high_level_description":"a cat"}""").Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void JsonErrorDetail_EmptyPrompt_SaysSo(string? text) =>
        GenerationReadiness.JsonErrorDetail(text).Should().Be("prompt is empty");

    [Fact]
    public void JsonErrorDetail_BrokenJson_GivesLineAndPositionWithoutTheRawTail()
    {
        var detail = GenerationReadiness.JsonErrorDetail("{\"a\": 1,\n\"b\": }");

        detail.Should().NotBeNull();
        detail.Should().Contain("(line 2, pos ");
        detail.Should().NotContain("LineNumber:");
    }
}
