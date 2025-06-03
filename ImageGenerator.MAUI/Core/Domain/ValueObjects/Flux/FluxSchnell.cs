using System.ComponentModel.DataAnnotations;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public class FluxSchnell : FluxBase
{
    public override required string ModelName { get; set; } = "flux-schnell";
    
    public bool GoFast { get; set; } = true;

    public string Megapixels { get; set; } = "1";

    public int NumOutputs { get; set; } = 1;

    [Range(ValidationConstants.StepsMin, ValidationConstants.StepsMax, ErrorMessage = "Number of inference steps must be between 1 and 150.")]
    public int NumInferenceSteps { get; set; } = ValidationConstants.StepsMin;

    public bool DisableSafetyChecker { get; set; } = false;

    public override string AspectRatio { get; set; } = "1:1";
}
