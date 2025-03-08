using System.ComponentModel.DataAnnotations;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models;

public abstract class FluxCommonBase
{
    [Required(ErrorMessage = "Prompt is mandatory for every Flux request.")]
    [StringLength(2000, ErrorMessage = "Prompt cannot exceed 2000 characters.")]
    public virtual string Prompt { get; set; }

    [Range(0, ValidationConstants.SeedMaxValue,
        ErrorMessage = "Seed must be between 0 and 4294967295.")]
    public virtual long? Seed { get; set; }

    [Required(ErrorMessage = "Output format is required.")]
    public virtual ImageOutputFormat OutputFormat { get; set; } = ImageOutputFormat.Webp;
}

