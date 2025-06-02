namespace ImageGenerator.MAUI.Models.Flux;

public class FluxDepthPro : FluxBase
{
    public override required string ModelName { get; set; }  = "flux-depth-pro";
    
    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 15;

    public string? ControlImage { get; set; }
}
