namespace ImageGenerator.MAUI.Models;

public class Flux11Pro : FluxCommonBase
{
    public int? Width { get; set; }

    public int? Height { get; set; }

    public string ImagePrompt { get; set; }

    public int OutputQuality { get; set; } = 80;

    public bool PromptUpsampling { get; set; } = false;

    public new string AspectRatio { get; set; } = "1:1";

    public new string OutputFormat { get; set; } = "webp";
}
