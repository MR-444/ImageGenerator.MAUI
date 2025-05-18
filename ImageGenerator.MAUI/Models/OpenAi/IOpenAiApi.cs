using Refit;

namespace ImageGenerator.MAUI.Models.OpenAi;

public interface IOpenAiApi
{
    // "model" is part of the route
    // We add a string parameter "bearerToken" for setting Authorization manually
    [Post("/v1/images/generations")]
    Task<OpenAiResponse> CreatePredictionAsync(
        [Header("Authorization")] string bearerToken,
        [AliasAs("model")] string model,
        [Body] OpenAiRequest request
    );
    
    // Get the prediction by its ID
    [Get("/v1/predictions/{predictionId}")]
    Task<OpenAiResponse?> GetPredictionAsync(
        [Header("Authorization")] string bearerToken,
        [AliasAs("predictionId")] string predictionId
    );
}