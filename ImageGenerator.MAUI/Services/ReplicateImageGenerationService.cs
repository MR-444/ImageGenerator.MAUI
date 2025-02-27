using System.Text;
using System.Text.Json;
using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

public class ReplicateImageGenerationService : IImageGenerationService
{
    private readonly HttpClient _httpClient;
    public ReplicateImageGenerationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, string apiToken)
    {
        // Build the request JSON from parameters.
        // (Example: create a request body, set headers, etc.)
        var requestBody = new
        {
            prompt = parameters.Prompt,
            steps = parameters.Steps,
            guidance = parameters.Guidance,
            aspect_ratio = parameters.AspectRatio,
            seed = parameters.Seed
        };

        var requestJson = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
        request.Headers.Add("Authorization", $"Token {apiToken}");
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // Deserialize the API response into your GeneratedImage model.
        // This step may involve extracting a URL and loading the image.
        var responseContent = await response.Content.ReadAsStringAsync();
        // (Assume you parse it into a GeneratedImage instance.)
        var generatedImage = new GeneratedImage
        {
            Image = ImageSource.FromUri(new Uri("https://example.com/generated.png")),
            Metadata = "Used parameters: ..." // Fill in as needed.
        };

        return generatedImage;
    }
}