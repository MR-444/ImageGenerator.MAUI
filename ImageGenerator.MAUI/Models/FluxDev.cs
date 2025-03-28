namespace ImageGenerator.MAUI.Models;

public class FluxDev : FluxBase
{
    public string Image { get; set; }

    public bool GoFast { get; set; } = true;

    public double Guidance { get; set; } = 3;

    public string Megapixels { get; set; } = "1";

    public int NumOutputs { get; set; } = 1;

    public int OutputQuality { get; set; } = 80;

    public double PromptStrength { get; set; } = 0.8;

    public int NumInferenceSteps { get; set; } = 28;

    public bool DisableSafetyChecker { get; set; } = false;

    public new string AspectRatio { get; set; } = "1:1";

    public override string Model { get; }
    public new string OutputFormat { get; set; } = "png";
}
