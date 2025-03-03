namespace ImageGenerator.MAUI.Models;

public class ReplicateInput
{
    public string Prompt { get; set; }
    public bool Prompt_Upsampling { get; set; }
    public long Seed { get; set; }
    public int Steps { get; set; }
    public double Guidance { get; set; }
    public double Interval { get; set; }
    public bool Raw { get; set; }
    public string Aspect_Ratio { get; set; }
    public string Image_Prompt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Safety_Tolerance { get; set; }
    public string Output_Format { get; set; }
    public int Output_Quality { get; set; }
    // Add any other fields relevant to your model
}

public class ReplicatePredictionRequest
{
    public ReplicateInput Input { get; set; }
}


public class ReplicatePredictionResponse
{
    public string? Output { get; set; }
}
