namespace ImageGenerator.MAUI.Models;

public class FluxSchnell : FluxCommonBase
{
    public bool GoFast { get; set; } = true;

    public string Megapixels { get; set; } = "1";

    public int NumOutputs { get; set; } = 1;

    public int OutputQuality { get; set; } = 80;

    public int NumInferenceSteps { get; set; } = 4;

    public bool DisableSafetyChecker { get; set; } = false;

    public new string AspectRatio { get; set; } = "1:1";

    public new string OutputFormat { get; set; } = "png";
}
