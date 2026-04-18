using ImageGenerator.MAUI.Models.Replicate;
using Refit;

namespace ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;

public interface IReplicateApi
{
    [Post("/v1/models/{model}/predictions")]
    [Headers("Prefer: wait")]
    Task<ReplicatePredictionResponse> CreatePredictionAsync(
        [Header("Authorization")] string bearerToken,
        [AliasAs("model")] string model,
        [Body] ReplicatePredictionRequest request,
        CancellationToken cancellationToken = default
    );

    [Get("/v1/predictions/{predictionId}")]
    Task<ReplicatePredictionResponse?> GetPredictionAsync(
        [Header("Authorization")] string bearerToken,
        [AliasAs("predictionId")] string predictionId,
        CancellationToken cancellationToken = default
    );
}
