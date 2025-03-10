namespace ImageGenerator.MAUI.Models;

public class FluxCannyPro : FluxBase
{
    public override string Model => "flux-canny-pro";
    
    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 30;

    public string ControlImage { get; set; }

    public bool PromptUpsampling { get; set; } = false;
}
