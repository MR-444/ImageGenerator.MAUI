using Refit;

namespace ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;

public interface IOpenAiApi
{
    [Post("/v1/images/generations")]
    Task<OpenAiResponse> CreatePredictionAsync(
        [Header("Authorization")] string bearerToken,
        [Body] OpenAiRequest request,
        CancellationToken cancellationToken = default
    );
}
