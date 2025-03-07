using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services
{
    public class ReplicateImageGenerationService(IReplicateApi replicateApi) : IImageGenerationService
    {
        public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
        {
            try
            {
                // Validate all input parameters
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
                    parameters.Seed = new Random().NextInt64(0, ValidationConstants.SeedMaxValue);
                }

                // Make the call to the generation model
                var finalResponse = await CallReplicateModelAsync(parameters);

                var imageUrl = finalResponse.Output;
                // Download the resulting image
                var filePath = await DownloadImageAsync(imageUrl!, parameters.OutputFormat);

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
        }

        // Validation logic
        private static string? ValidateParameters(ImageGenerationParameters p)
        {
            if (string.IsNullOrWhiteSpace(p.ApiToken))
                return "ApiToken cannot be empty.";
                
            // Prompt (required, cannot be empty)
            if (string.IsNullOrWhiteSpace(p.Prompt))
                return "Prompt cannot be empty.";
            
            // Aspect Ratio (required, cannot be empty, optionally check for valid values)
            if (string.IsNullOrWhiteSpace(p.AspectRatio))
                return "Aspect Ratio cannot be empty.";
            
            // Image Prompt (required, cannot be empty)
            if (string.IsNullOrWhiteSpace(p.ImagePrompt) && string.IsNullOrWhiteSpace(p.Prompt) )
                return "Image Prompt cannot be empty, if no prompt is provided.";

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

        
        private async Task<ReplicatePredictionResponse> CallReplicateModelAsync(ImageGenerationParameters parameters)
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
            var response = await replicateApi.CreatePredictionAsync(
                bearerToken,
                parameters.Model,
                replicateRequest
            );
            
            // Poll for final output, using the returned prediction ID
            var finalResponse = await ReplicateHelper.PollForOutputAsync(
                replicateApi,
                bearerToken,
                response.Id ?? string.Empty
            );

            // Return the single URL string
            return finalResponse;
        }
        
        

        // Download the image from the returned URL
        private static async Task<string> DownloadImageAsync(string imageUrl, string format)
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
