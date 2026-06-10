namespace ImageGenerator.MAUI.Shared.Constants;

public static class ModelConstants
{
    public static class OpenAI
    {
        public const string GptImage15OnReplicate = "openai/gpt-image-1.5";
        public const string GptImage2OnReplicate = "openai/gpt-image-2";
    }

    public static class Flux
    {
        public const string Pro11 = "black-forest-labs/flux-1.1-pro";
        public const string Pro11Ultra = "black-forest-labs/flux-1.1-pro-ultra";

        public const string Klein4b = "black-forest-labs/flux-2-klein-4b";
        public const string Flex2 = "black-forest-labs/flux-2-flex";
        public const string Pro2 = "black-forest-labs/flux-2-pro";
        public const string Max2 = "black-forest-labs/flux-2-max";
    }

    public static class Google
    {
        public const string NanoBanana2 = "google/nano-banana-2";
    }

    public static class Ideogram
    {
        // The V4 family is not in Replicate's curated text-to-image collection, so these are
        // pinned as seeds (IdeogramV4Descriptors) rather than surfaced by Refresh Models.
        public const string V4Balanced = "ideogram-ai/ideogram-v4-balanced";
        public const string V4Turbo = "ideogram-ai/ideogram-v4-turbo";
        public const string V4Quality = "ideogram-ai/ideogram-v4-quality";
    }

    public static class Pollinations
    {
        public const string PrefixSlash = "pollinations/";

        // Seed list: the free image-producing models on gen.pollinations.ai/models that the
        // user actually generates with. Live catalog fetch surfaces any additional free models.
        public const string Flux = "pollinations/flux";
        public const string Zimage = "pollinations/zimage";
        public const string QwenImage = "pollinations/qwen-image";

        public static bool IsId(string? modelId) =>
            !string.IsNullOrEmpty(modelId)
            && modelId.StartsWith(PrefixSlash, StringComparison.Ordinal);
    }

    public static class ComfyUi
    {
        // "comfyui/<workflow-file-stem>" — the stem round-trips to an API-format workflow
        // export in OutputPaths.ComfyWorkflowsDirectory; there is no remote model catalog.
        public const string PrefixSlash = "comfyui/";

        // ComfyUI's own default bind. A LAN instance (started with --listen) or a proxied
        // https endpoint is configured per-user via the ComfyUI server setting.
        public const string DefaultBaseUrl = "http://127.0.0.1:8188";

        public static bool IsId(string? modelId) =>
            !string.IsNullOrEmpty(modelId)
            && modelId.StartsWith(PrefixSlash, StringComparison.Ordinal);

        public static string WorkflowName(string modelId) =>
            IsId(modelId) ? modelId[PrefixSlash.Length..] : modelId;
    }
}
