using Refit;

namespace ImageGenerator.MAUI.Models;

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
}
