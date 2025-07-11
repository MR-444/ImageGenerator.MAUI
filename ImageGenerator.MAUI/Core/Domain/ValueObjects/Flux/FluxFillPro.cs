namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public class FluxFillPro : FluxBase
{
    public override required string ModelName { get; set; } = "flux-fill-pro";
    
    public string? Image { get; set; }

    public string? Mask { get; set; }

    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 60;

    public string Outpaint { get; set; } = "None";
}
