namespace ImageGenerator.MAUI.Models;

public class FluxFillPro : FluxBase
{
    public string Image { get; set; }

    public string Mask { get; set; }

    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 60;

    public string Outpaint { get; set; } = "None";

    public bool PromptUpsampling { get; set; } = false;

    public override string Model { get; }
    
    public new string OutputFormat { get; set; } = "png";
}
