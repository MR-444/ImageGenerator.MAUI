using Refit;

namespace ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;

public interface IOpenAiApi
{
    // Create image
    // We add a string parameter "bearerToken" for setting Authorization manually
    [Post("/v1/images/generations")]
    Task<OpenAiResponse> CreatePredictionAsync(
        [Header("Authorization")] string bearerToken,
        [Body] OpenAiRequest request
    );
    
    // Create image edit
    [Post("/v1/images/edits")]
    Task<OpenAiResponse?> GetPredictionAsync(
        [Header("Authorization")] string bearerToken
    );
}