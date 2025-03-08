using System.ComponentModel.DataAnnotations;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models;

public class Flux11Pro : FluxCommonBase
{
    public string ImagePrompt { get; set; }

    [Range(ValidationConstants.StepsMin, ValidationConstants.StepsMax, 
        ErrorMessage = "Steps must be between {1} and {2}.")]
    public int Steps { get; set; } = 25;

    [Range(ValidationConstants.GuidanceMin, ValidationConstants.GuidanceMax, 
        ErrorMessage = "Guidance scale must be between {1} and {2}.")]
    public int Guidance { get; set; } = 7;

    [Range(ValidationConstants.IntervalMin, ValidationConstants.IntervalMax, 
        ErrorMessage = "Interval must be between {1} and {2}.")]
    public int Interval { get; set; } = 5;

    [Range(ValidationConstants.SafetyMin, ValidationConstants.SafetyMax, 
        ErrorMessage = "Safety tolerance must be between {1} and {2}.")]
    public int SafetyTolerance { get; set; } = 2;

    [Range(ValidationConstants.OutputQualityMin, ValidationConstants.OutputQualityMax, 
        ErrorMessage = "Output quality must be between {1} and {2}.")]
    public int OutputQuality { get; set; } = 80;

    [Required(ErrorMessage = "Aspect Ratio must be explicitly defined.")]
    public AspectRatioType AspectRatio { get; set; } = AspectRatioType.Square_1_1;

    [Range(ValidationConstants.ImageWidthMin, ValidationConstants.ImageWidthMax,
        ErrorMessage = "Width must be between {1} and {2}.")]
    public int? Width { get; set; }

    [Range(ValidationConstants.ImageHeightMin, ValidationConstants.ImageHeightMax,
        ErrorMessage = "Height must be between {1} and {2}.")]
    public int? Height { get; set; }

    public bool PromptUpsampling { get; set; } = false;
}

