using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.OpenAi;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models.Factories;

/// <summary>
/// A factory class for creating image model instances based on the specified image generation parameters.
/// </summary>
/// <remarks>
/// This factory provides a centralized mechanism to construct image models that correspond
/// to particular image generation requirements. It abstracts the logic required to select
/// and instantiate the appropriate model type based on the provided parameters.
/// This approach promotes consistency and maintainability in model creation and usage.
/// </remarks>
/// <example>
/// The factory is utilized by services, including OpenAiImageGenerationService and others,
/// to generate image models tailored for specific image processing operations.
/// </example>
public static class ImageModelFactory
{
    /// <summary>
    /// Creates an instance of an image model based on the provided image generation parameters.
    /// </summary>
    /// <param name="parameters">The parameters used to generate the image model.</param>
    /// <returns>An instance of <see cref="ImageModelBase"/> configured according to the provided parameters.</returns>
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
                InputImage = parameters.ImagePrompt
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