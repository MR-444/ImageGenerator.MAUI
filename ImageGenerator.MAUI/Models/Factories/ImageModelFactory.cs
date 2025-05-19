using ImageGenerator.MAUI.Models.Flux;

namespace ImageGenerator.MAUI.Models.Factories;

public class ImageModelFactory
{
    public static ImageModelBase CreateImageModel(ImageGenerationParameters parameters)
    {
        return parameters.Model switch
        {
            "black-forest-labs/flux-1.1-pro" => new Flux11Pro
            {
                ModelName = parameters.Model,
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
            },
            "flux-1.1-pro-ultra" => new Flux11ProUltra
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat
            },
            // Add other model types here similarly
            _ => throw new ArgumentException($"Unknown model type: {parameters.Model}")
        };
    }
}