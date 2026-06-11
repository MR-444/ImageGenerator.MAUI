using ImageGenerator.MAUI.Models.Replicate;
using Refit;

namespace ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;

public interface IReplicateApi
{
    // No `Prefer: wait` — that held the connection open up to 60s and sometimes replied
    // 201 Created with an empty body, which Refit then rejects. Always poll instead.
    [Post("/v1/models/{model}/predictions")]
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

    [Get("/v1/collections/text-to-image")]
    Task<ReplicateCollectionResponse> GetTextToImageCollectionAsync(
        [Header("Authorization")] string bearerToken,
        CancellationToken cancellationToken = default
    );
}
