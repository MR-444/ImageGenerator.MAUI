using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Vision;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class OpenRouterModelCatalogTests
{
    private const string CatalogJson =
        """
        {
          "data": [
            {
              "id": "free/vision:free",
              "name": "Free Vision",
              "architecture": {
                "input_modalities": ["text", "image"],
                "output_modalities": ["text"]
              },
              "pricing": { "prompt": "0", "completion": "0" }
            },
            {
              "id": "paid/vision",
              "name": "Paid Vision",
              "architecture": {
                "input_modalities": ["image", "text"],
                "output_modalities": ["text"]
              },
              "pricing": { "prompt": "0.000001", "completion": "0.000002" }
            },
            {
              "id": "free/text-only:free",
              "name": "Free Text Only",
              "architecture": {
                "input_modalities": ["text"],
                "output_modalities": ["text"]
              },
              "pricing": { "prompt": "0", "completion": "0" }
            },
            {
              "id": "free/image-output-only:free",
              "name": "Free Image Output Only",
              "architecture": {
                "input_modalities": ["text", "image"],
                "output_modalities": ["image"]
              },
              "pricing": { "prompt": "0", "completion": "0" }
            }
          ]
        }
        """;

    [Fact]
    public void ParseVisionModels_ReturnsImageInputTextOutputModels()
    {
        var models = OpenRouterModelCatalog.ParseVisionModels(CatalogJson, freeOnly: false);

        models.Select(m => m.Id).Should().Equal("free/vision:free", "paid/vision");
    }

    [Fact]
    public void ParseVisionModels_FreeOnly_DropsPaidModels()
    {
        var models = OpenRouterModelCatalog.ParseVisionModels(CatalogJson, freeOnly: true);

        models.Select(m => m.Id).Should().Equal("free/vision:free");
        models[0].IsFree.Should().BeTrue();
    }
}
