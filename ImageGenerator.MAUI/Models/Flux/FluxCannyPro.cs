namespace ImageGenerator.MAUI.Models.Flux;

public class FluxCannyPro : FluxBase
{
    public override required string ModelName { get; set; }  = "flux-canny-pro";

    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 30;

    public string? ControlImage { get; set; }
}
