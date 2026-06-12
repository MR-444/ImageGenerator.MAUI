using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Services;

namespace ImageGenerator.MAUI.Tests.Services.Civitai;

public class CivitaiModelReferenceTests
{
    [Fact]
    public void ParseVersionId_CreatePostUrl_ExtractsVersionId()
    {
        // The exact "create post for this model" URL the model page hands out (Ideogram 4).
        // The encoded returnUrl also contains modelVersionId%3D… — the UNENCODED query
        // parameter must win.
        const string url = "https://civitai.com/posts/create?modelId=2676710&modelVersionId=3005491&returnUrl=%2Fmodels%2F2676710%2Fideogram-4%3FmodelVersionId%3D3005491";

        CivitaiModelReference.ParseVersionId(url).Should().Be(3005491);
    }

    [Fact]
    public void ParseVersionId_ModelPageUrl_ExtractsVersionId()
    {
        CivitaiModelReference.ParseVersionId("https://civitai.com/models/2676710/ideogram-4?modelVersionId=3005491")
            .Should().Be(3005491);
    }

    [Fact]
    public void ParseVersionId_BareNumber_ParsesIt()
    {
        CivitaiModelReference.ParseVersionId(" 3005491 ").Should().Be(3005491);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://civitai.com/models/2676710/ideogram-4")] // model URL without a version
    [InlineData("not a reference")]
    public void ParseVersionId_Unrecognizable_ReturnsNull(string? text)
    {
        CivitaiModelReference.ParseVersionId(text).Should().BeNull();
    }
}
