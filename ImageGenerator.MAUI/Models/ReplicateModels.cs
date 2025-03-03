using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models;

public class ReplicateInput
{
    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }
    
    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }
    
    [JsonPropertyName("seed")]
    public required long Seed { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public required string AspectRatio { get; set; }
    
    [JsonPropertyName("image_prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImagePrompt
    {
        get => _imagePrompt;
        set => _imagePrompt = string.IsNullOrWhiteSpace(value) ? null : value;
    }
    private string? _imagePrompt;
    
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
}

public class ReplicatePredictionRequest
{
    public required ReplicateInput Input { get; set; }
}


public class ReplicatePredictionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("input")]
    public Dictionary<string, object>? Input { get; set; }

    [JsonPropertyName("logs")]
    public string? Logs { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("data_removed")]
    public bool? DataRemoved { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("metrics")]
    public ReplicateResponseMetrics? Metrics { get; set; }

    [JsonPropertyName("urls")]
    public ReplicateResponseUrls? Urls { get; set; }
}

public class ReplicateResponseMetrics
{
    [JsonPropertyName("image_count")]
    public int? ImageCount { get; set; }

    [JsonPropertyName("predict_time")]
    public double? PredictTime { get; set; }

    [JsonPropertyName("total_time")]
    public double? TotalTime { get; set; }
}

public class ReplicateResponseUrls
{
    [JsonPropertyName("cancel")]
    public string? Cancel { get; set; }

    [JsonPropertyName("get")]
    public string? Get { get; set; }

    [JsonPropertyName("stream")]
    public string? Stream { get; set; }
}

