using System.ComponentModel.DataAnnotations;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models.Flux;

public class FluxSchnell : FluxBase
{
    public override required string ModelName { get; set; } = "flux-schnell";
    
    public bool GoFast { get; set; } = true;

    public string Megapixels { get; set; } = "1";

    public int NumOutputs { get; set; } = 1;

    [Range(ValidationConstants.OutputQualityMin, ValidationConstants.OutputQualityMax, ErrorMessage = "Output quality must be between 1 and 100.")]
    public int OutputQuality { get; set; } = ValidationConstants.OutputQualityMax;

    [Range(ValidationConstants.StepsMin, ValidationConstants.StepsMax, ErrorMessage = "Number of inference steps must be between 1 and 150.")]
    public int NumInferenceSteps { get; set; } = ValidationConstants.StepsMin;

    public bool DisableSafetyChecker { get; set; } = false;

    public override string AspectRatio { get; set; } = "1:1";
}
