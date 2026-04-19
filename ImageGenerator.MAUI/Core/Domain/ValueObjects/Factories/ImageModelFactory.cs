using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Factories;

public static class ImageModelFactory
{
    public static object CreateImageModel(Core.Domain.Entities.ImageGenerationParameters parameters)
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
            ModelConstants.OpenAI.GptImage1 => new OpenAiRequest
            {
               ModelName = parameters.Model,
               Prompt = parameters.Prompt,
               // gpt-image-1 takes explicit size strings: 1024x1024, 1536x1024, 1024x1536, or auto.
               // The VM's AspectRatio picker already constrains the choices to that set for this model.
               Size = parameters.AspectRatio
            },

            // Flux 2 family (schema from GET /v1/models/black-forest-labs/flux-2-klein-4b).
            // No safety_tolerance / prompt_upsampling — the model rejects them. Null values
            // are stripped at the serializer layer (NullSkippingDictionaryConverter), so
            // Replicate sees the key omitted rather than `"images": null` (422s).
            ModelConstants.Flux.Klein4b
                or ModelConstants.Flux.Flex2
                or ModelConstants.Flux.Pro2
                or ModelConstants.Flux.Max2 => new Dictionary<string, object?>
                {
                    ["prompt"] = parameters.Prompt,
                    ["seed"] = parameters.Seed,
                    ["aspect_ratio"] = parameters.AspectRatio,
                    ["output_format"] = parameters.OutputFormat.ToString().ToLowerInvariant(),
                    ["output_quality"] = parameters.OutputQuality,
                    ["images"] = string.IsNullOrEmpty(parameters.ImagePrompt)
                        ? null
                        : new[] { ReplicateImageEncoding.BuildDataUri(parameters.ImagePrompt) }
                },

            // Replicate-hosted openai/gpt-image-1.5 — narrow AR enum, its own field names.
            ModelConstants.OpenAI.GptImage15OnReplicate => new Dictionary<string, object?>
                {
                    ["prompt"] = parameters.Prompt,
                    ["aspect_ratio"] = parameters.AspectRatio,
                    ["output_format"] = parameters.OutputFormat.ToString().ToLowerInvariant() switch
                    {
                        "jpg" => "jpeg",
                        var fmt => fmt
                    },
                    ["output_compression"] = parameters.OutputQuality,
                    ["quality"] = parameters.GptQuality,
                    ["background"] = parameters.GptBackground,
                    ["moderation"] = parameters.GptModeration,
                    ["input_fidelity"] = parameters.GptInputFidelity,
                    ["input_images"] = string.IsNullOrEmpty(parameters.ImagePrompt)
                        ? null
                        : new[] { ReplicateImageEncoding.BuildDataUri(parameters.ImagePrompt) }
                },

            // google/nano-banana-2 — no seed, uses `image_input`, wider AR enum,
            // `resolution` knob (1K/2K/4K), rejects webp.
            ModelConstants.Google.NanoBanana2 => new Dictionary<string, object?>
                {
                    ["prompt"] = parameters.Prompt,
                    ["aspect_ratio"] = parameters.AspectRatio,
                    ["resolution"] = parameters.Resolution,
                    ["output_format"] = parameters.OutputFormat.ToString().ToLowerInvariant() switch
                    {
                        "webp" => "jpg",
                        var fmt => fmt
                    },
                    ["image_input"] = string.IsNullOrEmpty(parameters.ImagePrompt)
                        ? null
                        : new[] { ReplicateImageEncoding.BuildDataUri(parameters.ImagePrompt) }
                    // google_search / image_search stay at API defaults (false/false).
                },

            // Fallback for any other `{owner}/{name}` surfaced by Refresh Models.
            // Conservative field set: prompt + seed + aspect_ratio + output_format + output_quality
            // (the lowest-common-denominator across recent Replicate text-to-image models). May still
            // 422 on models that use different field names — that's the signal to add an explicit
            // case above.
            _ when LooksLikeReplicatePath(parameters.Model) => new Dictionary<string, object?>
                {
                    ["prompt"] = parameters.Prompt,
                    ["seed"] = parameters.Seed,
                    ["aspect_ratio"] = parameters.AspectRatio,
                    ["output_format"] = parameters.OutputFormat.ToString().ToLowerInvariant(),
                    ["output_quality"] = parameters.OutputQuality
                },

            _ => throw new ArgumentException($"Unknown model type: {parameters.Model}")
        };
    }

    private static bool LooksLikeReplicatePath(string modelName)
    {
        var slash = modelName.IndexOf('/');
        return slash > 0 && slash < modelName.Length - 1;
    }
}
