using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;
using Refit;

namespace ImageGenerator.MAUI.Services
{
    public class ReplicateImageGenerationService : IImageGenerationService
    {
        // You might store these in a config or constants class
        private const long seedMaxValue = 4294967295;
        
        // Store the injected Refit interface
        private readonly IReplicateApi _replicateApi;

        public ReplicateImageGenerationService(IReplicateApi replicateApi)
        {
            _replicateApi = replicateApi;
        }


        public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
        {
            try
            {
                // Validate
                var validationError = ValidateParameters(parameters);
                if (validationError != null)
                {
                    return new GeneratedImage
                    {
                        Message = $"Validation Error: {validationError}",
                        FilePath = null,
                        UpdatedSeed = parameters.Seed
                    };
                }

                // Potentially randomize the seed
                if (parameters.RandomizeSeed)
                {
                    parameters.Seed = new Random().NextInt64(0, seedMaxValue);
                }

                // Here you would set API token environment variable, if needed
                // Environment.SetEnvironmentVariable("REPLICATE_API_TOKEN", parameters.ApiToken);

                // Make the call to your generation model
                var imageUrl = await CallReplicateModelAsync(parameters);

                // Download the resulting image
                var filePath = await DownloadImageAsync(imageUrl, parameters.OutputFormat, parameters.OutputQuality);

                // Optionally embed metadata or do other post-processing

                return new GeneratedImage
                {
                    Message = $"Image generated successfully with model {parameters.Model}.",
                    FilePath = filePath,
                    UpdatedSeed = parameters.Seed
                };
            }
            catch (Exception ex)
            {
                return new GeneratedImage
                {
                    Message = $"An error occurred: {ex.Message}",
                    FilePath = null,
                    UpdatedSeed = parameters.Seed
                };
            }
            finally
            {
               // Clear the API token from environment if needed
               Environment.SetEnvironmentVariable("REPLICATE_API_TOKEN", null);
            }
        }

        // Validation logic (similar to your Python checks)
        private static string? ValidateParameters(ImageGenerationParameters p)
        {
            // Seed
            if (p.Seed < 0 || p.Seed > ValidationConstants.SeedMaxValue)
                return $"Seed must be between 0 and {ValidationConstants.SeedMaxValue}.";

            // Steps
            if (p.Steps < ValidationConstants.SliderStepsMin || p.Steps > ValidationConstants.SliderStepsMax)
                return $"Steps must be between {ValidationConstants.SliderStepsMin} and {ValidationConstants.SliderStepsMax}.";

            // Guidance
            if (p.Guidance < ValidationConstants.SliderGuidanceMin || p.Guidance > ValidationConstants.SliderGuidanceMax)
                return $"Guidance must be between {ValidationConstants.SliderGuidanceMin} and {ValidationConstants.SliderGuidanceMax}.";

            // Safety Tolerance
            if (p.SafetyTolerance < ValidationConstants.SliderSafetyMin || p.SafetyTolerance > ValidationConstants.SliderSafetyMax)
                return $"Safety Tolerance must be between {ValidationConstants.SliderSafetyMin} and {ValidationConstants.SliderSafetyMax}.";

            // Interval
            if (p.Interval < ValidationConstants.SliderIntervalMin || p.Interval > ValidationConstants.SliderIntervalMax)
                return $"Interval must be between {ValidationConstants.SliderIntervalMin} and {ValidationConstants.SliderIntervalMax}.";

            // Width
            if (p.Width < ValidationConstants.ImageWidthMin || p.Width > ValidationConstants.ImageWidthMax)
                return $"Width must be between {ValidationConstants.ImageWidthMin} and {ValidationConstants.ImageWidthMax}.";
            if (p.Width % 16 != 0)
                return "Width must be a multiple of 16.";

            // Height
            if (p.Height < ValidationConstants.ImageHeightMin || p.Height > ValidationConstants.ImageHeightMax)
                return $"Height must be between {ValidationConstants.ImageHeightMin} and {ValidationConstants.ImageHeightMax}.";
            if (p.Height % 16 != 0)
                return "Height must be a multiple of 16.";

            // Output Quality
            if (p.OutputQuality < ValidationConstants.SliderOutputQualityMin || p.OutputQuality > ValidationConstants.SliderOutputQualityMax)
                return $"Output Quality must be between {ValidationConstants.SliderOutputQualityMin} and {ValidationConstants.SliderOutputQualityMax}.";

            // All validations passed
            return null; 
        }

        
        private async Task<string> CallReplicateModelAsync(ImageGenerationParameters parameters)
        {
            // Build the request payload
            var replicateRequest = new ReplicatePredictionRequest
            {
                Input = new ReplicateInput
                {
                    Prompt = parameters.Prompt,
                    PromptUpsampling = parameters.PromptUpsampling,
                    Seed = parameters.Seed,
                    Width = parameters.Width,
                    Height = parameters.Height,
                    AspectRatio = parameters.AspectRatio,
                    ImagePrompt = parameters.ImagePrompt,
                    SafetyTolerance = parameters.SafetyTolerance,
                    OutputFormat = parameters.OutputFormat,
                    OutputQuality = parameters.OutputQuality
                }
            };

            // Construct the bearer token string once
            var bearerToken = $"Bearer {parameters.ApiToken}";

            
            // Invoke the endpoint using the injected Refit interface
            var response = await _replicateApi.CreatePredictionAsync(
                bearerToken,
                parameters.Model,
                replicateRequest
            );


            // Check if we have a valid single URL in “Output”
            if (string.IsNullOrWhiteSpace(response.Output))
            {
                throw new Exception("No valid output URL found in the Replicate response.");
            }

            // Return the single URL string
            return response.Output;
        }
        
        

        // Download the image from the returned URL
        private static async Task<string> DownloadImageAsync(string imageUrl, string format, int quality)
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();

            // Save the image to local storage
            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            var fileName = $"image_{DateTime.Now:yyyyMMdd_HHmmss}.{format}";
            var savePath = Path.Combine(FileSystem.Current.AppDataDirectory, fileName);

            await File.WriteAllBytesAsync(savePath, imageBytes);

            return savePath;
        }
    }
}
