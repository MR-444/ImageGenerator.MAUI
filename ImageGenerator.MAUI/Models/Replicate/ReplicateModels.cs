using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Models.Flux;

namespace ImageGenerator.MAUI.Models.Replicate;

public class ReplicatePredictionRequest
{
    public required FluxPro11 Input { get; set; }
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

