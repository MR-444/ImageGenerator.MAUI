using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models;

public class ReplicateInput
{
    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }
    
    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }
    
    [JsonPropertyName("seed")]
    public long Seed { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public required string AspectRatio { get; set; }
    
    [JsonPropertyName("image_prompt")]
    public required string ImagePrompt { get; set; }
    
    [JsonPropertyName("width")]
    public int Width { get; set; }
    
    [JsonPropertyName("height")]
    public int Height { get; set; }
    
    [JsonPropertyName("safety_tolerance")]
    public int SafetyTolerance { get; set; }
    
    [JsonPropertyName("output_format")]
    public required string OutputFormat { get; set; }
    
    [JsonPropertyName("output_quality")]
    public int OutputQuality { get; set; }
    // Add any other fields relevant to your model
    public int Steps { get; set; }
    public double Guidance { get; set; }
    public double Interval { get; set; }
    public bool Raw { get; set; }
}

public class ReplicatePredictionRequest
{
    public required ReplicateInput Input { get; set; }
}


public class ReplicatePredictionResponse
{
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }
}
