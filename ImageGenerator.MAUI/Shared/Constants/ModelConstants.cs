namespace ImageGenerator.MAUI.Shared.Constants;

public static class ModelConstants
{
    public static class OpenAI
    {
        // Legacy: the only value that routes to the native OpenAI API client.
        public const string GptImage1 = "openAI/gpt-image-1";

        // Replicate-hosted OpenAI model (routes through the Replicate client).
        public const string GptImage15OnReplicate = "openai/gpt-image-1.5";
    }

    public static class Flux
    {
        public const string Dev = "black-forest-labs/flux-dev";
        public const string Pro11 = "black-forest-labs/flux-1.1-pro";
        public const string Schnell = "black-forest-labs/flux-schnell";
        public const string Pro11Ultra = "black-forest-labs/flux-1.1-pro-ultra";
        public const string KontextMax = "black-forest-labs/flux-kontext-max";
        public const string KontextPro = "black-forest-labs/flux-kontext-pro";

        public const string Klein4b = "black-forest-labs/flux-2-klein-4b";
        public const string Flex2 = "black-forest-labs/flux-2-flex";
        public const string Pro2 = "black-forest-labs/flux-2-pro";
        public const string Max2 = "black-forest-labs/flux-2-max";
    }
} 