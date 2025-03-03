namespace ImageGenerator.MAUI.Models;

public class ReplicateInput
{
    public required string Prompt { get; set; }
    public bool PromptUpsampling { get; set; }
    public long Seed { get; set; }
    public int Steps { get; set; }
    public double Guidance { get; set; }
    public double Interval { get; set; }
    public bool Raw { get; set; }
    public required string AspectRatio { get; set; }
    public required string ImagePrompt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int SafetyTolerance { get; set; }
    public required string OutputFormat { get; set; }
    public int OutputQuality { get; set; }
    // Add any other fields relevant to your model
}

public class ReplicatePredictionRequest
{
    public required ReplicateInput Input { get; set; }
}


public class ReplicatePredictionResponse
{
    public string? Output { get; set; }
}
