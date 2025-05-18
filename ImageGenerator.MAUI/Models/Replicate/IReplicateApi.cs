using Refit;

namespace ImageGenerator.MAUI.Models.Replicate;

public interface IReplicateApi 
{
    // "model" is part of the route
    // We add a string parameter "bearerToken" for setting Authorization manually
    [Post("/v1/models/{model}/predictions")]
    [Headers("Prefer: wait")] 
    Task<ReplicatePredictionResponse> CreatePredictionAsync(
        [Header("Authorization")] string bearerToken,
        [AliasAs("model")] string model,
        [Body] ReplicatePredictionRequest request
    );
    
    // Get the prediction by its ID
    [Get("/v1/predictions/{predictionId}")]
    Task<ReplicatePredictionResponse?> GetPredictionAsync(
        [Header("Authorization")] string bearerToken,
        [AliasAs("predictionId")] string predictionId
    );
}
