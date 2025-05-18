namespace ImageGenerator.MAUI.Models.Flux;

public class FluxDepthPro : FluxBase
{
    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 15;

    public string ControlImage { get; set; }

    public bool PromptUpsampling { get; set; } = false;

    public override string Model { get; }
    
    public new string OutputFormat { get; set; } = "jpg";
}
