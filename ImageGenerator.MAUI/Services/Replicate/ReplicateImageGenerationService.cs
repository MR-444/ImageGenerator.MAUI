using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.Factories;
using ImageGenerator.MAUI.Models.Replicate;
using ImageGenerator.MAUI.Models.Flux;

namespace ImageGenerator.MAUI.Services.Replicate;

/// <summary>
/// Provides functionality for generating images using the Replicate API service.
/// </summary>
public class ReplicateImageGenerationService : IReplicateImageGenerationService
{

    private readonly HttpClient _httpClient;
    private readonly IReplicateApi _replicateApi;

    /// A service responsible for generating images using the Replicate API.
    /// This service implements the `IReplicateImageGenerationService` interface and provides functionality to create
    /// predictions for image generation using the external Replicate API. It also contains methods for additional operations
    /// like downloading the generated image as a Base64 encoded string.
    public ReplicateImageGenerationService(IReplicateApi replicateApi, HttpClient httpClient)
    {
        _replicateApi = replicateApi ?? throw new ArgumentNullException(nameof(replicateApi));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// Generates an image asynchronously based on the specified generation parameters.
    /// <param name="parameters">The parameters required to generate the image, including model details and image prompt information.</param>
    /// <returns>A GeneratedImage object containing the result of the image generation process, including any relevant messages and the generated image data in base64 format.</returns>
    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        try
        {
            // Use the factory to create the appropriate image model instance
            var imageModel = ImageModelFactory.CreateImageModel(parameters);

            // If we have a base64 image, convert it to data URI format for Kontext models
            if (!string.IsNullOrEmpty(parameters.ImagePrompt))
            {
                if (imageModel is FluxKontextPro kontextPro)
                {
                    // Check if the ImagePrompt is already a data URI
                    if (parameters.ImagePrompt.StartsWith("data:"))
                    {
                        kontextPro.InputImage = parameters.ImagePrompt;
                    }
                    else
                    {
                        // Detect image format from base64 data or use PNG as default
                        var mimeType = DetectImageMimeType(parameters.ImagePrompt);
                        kontextPro.InputImage = $"data:{mimeType};base64,{parameters.ImagePrompt}";
                    }
                }
                else if (imageModel is FluxKontextMax kontextMax)
                {
                    // Check if the ImagePrompt is already a data URI
                    if (parameters.ImagePrompt.StartsWith("data:"))
                    {
                        kontextMax.InputImage = parameters.ImagePrompt;
                    }
                    else
                    {
                        // Detect image format from base64 data or use PNG as default
                        var mimeType = DetectImageMimeType(parameters.ImagePrompt);
                        kontextMax.InputImage = $"data:{mimeType};base64,{parameters.ImagePrompt}";
                    }
                }
            }

            // Make the call to the generation model
            var finalResponse = await CallReplicateModelAsync(parameters, imageModel);
            if(finalResponse?.Output == null)
            {
                var errorMessage = finalResponse?.Error ?? "Unknown error";
                var status = finalResponse?.Status ?? "Unknown status";
                throw new InvalidOperationException($"Model prediction failed or returned no result. Status: {status}, Error: {errorMessage}");
            }

            var imageUrl = finalResponse.Output;
            var imageDataBase64 = await DownloadImageAsBase64Async(imageUrl!);

            return new GeneratedImage
            {
                Message = $"Image generated successfully with model {parameters.Model}.",
                FilePath = null,
                ImageDataBase64 = imageDataBase64
            };
        }
        catch (Exception ex)
        {
            return new GeneratedImage
            {
                Message = $"An error occurred: {ex.Message}",
                FilePath = null,
                ImageDataBase64 = null
            };
        }
    }

    /// Asynchronously makes a call to the Replicate API's model endpoint to initiate and retrieve a prediction result.
    /// This method constructs and sends the appropriate request payload and monitors for a finalized response.
    /// <param name="parameters">The parameters required for the image generation process, including the API token and model details.</param>
    /// <param name="imageModel">The specific image model instance to be used for the image generation request.</param>
    /// <returns>A task representing the asynchronous operation. The result contains the prediction response from the API, including the model's output.</returns>
    private async Task<ReplicatePredictionResponse?> CallReplicateModelAsync(ImageGenerationParameters parameters,
                                                                             ImageModelBase imageModel)
    {
        // Build the request payload
        var replicateRequest = new ReplicatePredictionRequest { Input = imageModel };

        // Construct the bearer token string once
        var bearerToken = $"Bearer {parameters.ApiToken}";

        // Invoke the endpoint using the injected Refit interface
        var response = await _replicateApi.CreatePredictionAsync(
            bearerToken,
            parameters.Model,
            replicateRequest
        );
            
        // Poll for final output, using the returned prediction ID
        var finalResponse = await ReplicateHelper.PollForOutputAsync(
            _replicateApi,
            bearerToken,
            response.Id ?? string.Empty
        );

        // Return the single URL string
        return finalResponse;
    }
        
    // Download the image from the returned URL
    /// Downloads an image from the specified URL and converts it to a Base64 encoded string.
    /// <param name="imageUrl">The URL of the image to be downloaded.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the Base64 encoded string of the downloaded image.</returns>
    protected virtual async Task<string> DownloadImageAsBase64Async(string imageUrl)
    {
        try
        {
            var response = await _httpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return Convert.ToBase64String(bytes);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to download image from {imageUrl}", ex);
        }
    }

    // Detect MIME type from base64 image data
    /// Detects the MIME type of an image based on its Base64-encoded data.
    /// <param name="base64Data">A string containing the Base64-encoded image data.</param>
    /// <returns>A string representing the detected MIME type of the image (e.g., "image/png", "image/jpeg"). Defaults to "image/png" if detection fails.</returns>
    private static string DetectImageMimeType(string base64Data)
    {
        try
        {
            // Decode the first few bytes to check the file signature
            var bytes = Convert.FromBase64String(base64Data.Substring(0, Math.Min(base64Data.Length, 20)));
            
            // Check for common image file signatures
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            
            if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return "image/gif";
            
            if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "image/webp";
        }
        catch
        {
            // If detection fails, fall back to PNG
        }
        
        // Default to PNG if detection fails
        return "image/png";
    }
}