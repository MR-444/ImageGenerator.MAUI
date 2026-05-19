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
}
