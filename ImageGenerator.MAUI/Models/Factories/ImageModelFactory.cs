using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.OpenAi;

namespace ImageGenerator.MAUI.Models.Factories;

public static class ImageModelFactory
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
            "black-forest-labs/flux-1.1-pro-ultra" => new Flux11ProUltra
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat,
                Raw = parameters.Raw,
                ImagePromptStrength = parameters.ImagePromptStrength
            },
            "black-forest-labs/flux-dev" => new FluxDev
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat,
                OutputQuality = parameters.OutputQuality
            },
            "black-forest-labs/flux-schnell" => new FluxSchnell
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat,
                OutputQuality = parameters.OutputQuality
            },
            // OpenAI models
            "gpt-image-1" => new OpenAiRequest
            {
               ModelName = parameters.Model,
               Prompt = parameters.Prompt
               // Everything else is optional, let us see if it's working.
            },
            _ => throw new ArgumentException($"Unknown model type: {parameters.Model}")
        };
    }
}