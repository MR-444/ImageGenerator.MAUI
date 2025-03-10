namespace ImageGenerator.MAUI.Models;

public class FluxCannyPro : FluxBase
{
    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 30;

    public string ControlImage { get; set; }

    public bool PromptUpsampling { get; set; } = false;

    public new string OutputFormat { get; set; } = "png";
}
