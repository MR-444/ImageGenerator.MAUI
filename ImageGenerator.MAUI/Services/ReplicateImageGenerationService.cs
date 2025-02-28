using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services
{
    public class ReplicateImageGenerationService : IImageGenerationService
    {
        // You might store these in a config or constants class
        private const long seedMaxValue = 4294967295;

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
        private string? ValidateParameters(ImageGenerationParameters p)
        {
            if (p.Seed < 0 || p.Seed > seedMaxValue)
                return $"Seed must be between 0 and {seedMaxValue}.";
            // Add further checks for steps, width, height, etc.
            // ...
            return null; 
        }

        // Dummy example of calling replicate’s model
        private Task<string> CallReplicateModelAsync(ImageGenerationParameters parameters)
        {
            // You’d replace this with the actual call to replicate.run or your chosen API endpoint
            // return the image URL
            return Task.FromResult("https://some-url-to-generated-image");
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
