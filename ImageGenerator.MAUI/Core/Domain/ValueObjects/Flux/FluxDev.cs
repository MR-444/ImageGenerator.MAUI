using System.ComponentModel.DataAnnotations;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public class FluxDev : FluxBase
{
    public override required string ModelName { get; set; } = "flux-dev";
    
    public string? Image { get; set; }

    public bool GoFast { get; set; } = true;

    [Range(ValidationConstants.GuidanceMin, ValidationConstants.GuidanceMax, ErrorMessage = "Guidance must be between 1 and 20.")]
    public double Guidance { get; set; } = ValidationConstants.GuidanceMin;

    public string Megapixels { get; set; } = "1";

    public int NumOutputs { get; set; } = 1;

    public double PromptStrength { get; set; } = 0.8;

    [Range(ValidationConstants.StepsMin, ValidationConstants.StepsMax, ErrorMessage = "Number of inference steps must be between 1 and 150.")]
    public int NumInferenceSteps { get; set; } = ValidationConstants.StepsMin;

    public bool DisableSafetyChecker { get; set; } = false;

    public new string AspectRatio { get; set; } = "1:1";
}
