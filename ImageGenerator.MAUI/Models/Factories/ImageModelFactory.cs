using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.OpenAi;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models.Factories;

public static class ImageModelFactory
{
    public static ImageModelBase CreateImageModel(ImageGenerationParameters parameters)
    {
        return parameters.Model switch
        {
            ModelConstants.Flux.Pro11 => new Flux11Pro
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                PromptUpsampling = parameters.PromptUpsampling,
                Seed = parameters.Seed,
                Width = parameters.AspectRatio == "custom" ? parameters.Width : null,
                Height = parameters.AspectRatio == "custom" ? parameters.Height : null,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat.ToString().ToLower(),
                OutputQuality = parameters.OutputQuality
            },
            ModelConstants.Flux.Pro11Ultra => new Flux11ProUltra
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat.ToString().ToLower(),
                Raw = parameters.Raw,
                ImagePromptStrength = parameters.ImagePromptStrength
            },
            ModelConstants.Flux.Dev => new FluxDev
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat.ToString().ToLower(),
                OutputQuality = parameters.OutputQuality
            },
            ModelConstants.Flux.Schnell => new FluxSchnell
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat.ToString().ToLower(),
                OutputQuality = parameters.OutputQuality
            },
            ModelConstants.Flux.KontextMax => new FluxKontextMax
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                InputImage = parameters.ImagePrompt // Using ImagePrompt as InputImage since they serve similar purposes
            },
            ModelConstants.Flux.KontextPro => new FluxKontextPro
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                InputImage = parameters.ImagePrompt // Using ImagePrompt as InputImage since they serve similar purposes
            },
            // OpenAI models
            ModelConstants.OpenAI.GptImage1 => new OpenAiRequest
            {
               ModelName = parameters.Model,
               Prompt = parameters.Prompt
               // Everything else is optional, let us see if it's working.
            },
            _ => throw new ArgumentException($"Unknown model type: {parameters.Model}")
        };
    }
}