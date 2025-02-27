namespace ImageGenerator.MAUI.Models;

public class ImageGenerationParameters
{
    public string Prompt { get; set; }
    public int Steps { get; set; }
    public double Guidance { get; set; }
    public string AspectRatio { get; set; }
    public int Seed { get; set; }

}