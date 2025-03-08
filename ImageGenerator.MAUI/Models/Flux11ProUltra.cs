namespace ImageGenerator.MAUI.Models;

public class Flux11ProUltra : FluxCommonBase
{
    public bool Raw { get; set; } = false;

    public string ImagePrompt { get; set; }

    public double ImagePromptStrength { get; set; } = 0.1;

    public new string AspectRatio { get; set; } = "1:1";

    public new string OutputFormat { get; set; } = "jpg";
}
