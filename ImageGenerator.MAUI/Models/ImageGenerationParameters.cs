namespace ImageGenerator.MAUI.Models;

public class ImageGenerationParameters
{
    public string ApiToken { get; set; } = string.Empty;
    public string Model { get; set; } = "black-forest-labs/flux-1.1-pro";
    public string Prompt { get; set; } = string.Empty;
    public long Seed { get; set; }
    public bool RandomizeSeed { get; set; } = true;
    public int Steps { get; set; } = 25;
    public double Guidance { get; set; } = 3.0;
    public string AspectRatio { get; set; } = "1:1";
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public int SafetyTolerance { get; set; } = 6;
    public double Interval { get; set; } = 2.0;
    public bool Raw { get; set; }
    public string OutputFormat { get; set; } = "png";
    public int OutputQuality { get; set; } = 100;
    public bool PromptUpsampling { get; set; }
}