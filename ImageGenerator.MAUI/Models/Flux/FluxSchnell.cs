namespace ImageGenerator.MAUI.Models.Flux;

public class FluxSchnell : FluxBase
{
    public override required string ModelName { get; set; } = "flux-schnell";
    
    public bool GoFast { get; set; } = true;

    public string Megapixels { get; set; } = "1";

    public int NumOutputs { get; set; } = 1;

    public int OutputQuality { get; set; } = 80;

    public int NumInferenceSteps { get; set; } = 4;

    public bool DisableSafetyChecker { get; set; } = false;

    public override string AspectRatio { get; set; } = "1:1";
}
