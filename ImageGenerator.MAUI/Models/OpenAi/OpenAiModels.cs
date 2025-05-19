using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models.OpenAi;

public class OpenAiRequest : ImageModelBase
{
        /// <summary>
        /// Required. A text description of the desired image(s). Maximum length is 32000 characters for GPT-Image-1.
        /// </summary>
        [JsonPropertyName("prompt")]
        public override required string Prompt { get; set; }

        /// <summary>
        /// Optional. Sets the transparency for the background. Defaults to "auto". 
        /// Possible values: transparent, opaque, auto.
        /// </summary>
        [JsonPropertyName("background")]
        public string? Background { get; set; } = "auto";

        /// <summary>
        /// Optional. Specifies the model to use for image generation.
        /// Defaults to "dall-e-2" but switches to "gpt-image-1" if parameters specific to it are used.
        /// </summary>
        [JsonPropertyName("model")]
        public override required string ModelName { get; set; } = "gpt-image-1";

        /// <summary>
        /// Optional. Controls the content moderation level. Defaults to "auto".
        /// Possible values: low, auto.
        /// </summary>
        [JsonPropertyName("moderation")]
        public string? Moderation { get; set; } = "auto";

        /// <summary>
        /// Optional. The number of images to generate. Defaults to 1. Must be between 1 and 10.
        /// </summary>
        [JsonPropertyName("n")]
        public int? Count { get; set; } = 1;

        /// <summary>
        /// Optional. The compression level (0-100%) for the generated images.
        /// Defaults to 100. Only supported with "webp" or "jpeg" output formats.
        /// </summary>
        [JsonPropertyName("output_compression")]
        public int? OutputCompression { get; set; } = 100;

        /// <summary>
        /// Optional. The format of the returned images. Defaults to "png".
        /// Possible values: png, jpeg, webp.
        /// </summary>
        [JsonPropertyName("output_format")]
        public string? OutputFormat { get; set; } = "png";

        /// <summary>
        /// Optional. Defines the quality of the generated image.
        /// Defaults to "auto". Possible values: auto, high, medium, low.
        /// </summary>
        [JsonPropertyName("quality")]
        public string? Quality { get; set; } = "auto";

        /// <summary>
        /// Required for GPT-Image-1. The format of the generated images (always base64-encoded).
        /// This value is constant for GPT-Image-1.
        /// </summary>
        [JsonPropertyName("response_format")]
        public string? ResponseFormat { get; set; } = "b64_json";

        /// <summary>
        /// Optional. The size of the generated image(s).
        /// Defaults to "auto". Possible values: 1024x1024, 1536x1024, 1024x1536, auto.
        /// </summary>
        [JsonPropertyName("size")]
        public string? Size { get; set; } = "auto";

        /// <summary>
        /// Optional. A unique identifier representing your end-user to monitor and detect abuse.
        /// </summary>
        [JsonPropertyName("user")]
        public string? User { get; set; }
}
public class OpenAiResponse
{
        /// <summary>
        /// The Unix timestamp when the image(s) were created.
        /// </summary>
        [JsonPropertyName("created")]
        public long Created { get; set; }

        /// <summary>
        /// A list of image data, where each entry contains a base64-encoded string representation of an image.
        /// </summary>
        [JsonPropertyName("data")]
        public required List<ImageData> Data { get; set; }

        /// <summary>
        /// The usage details including total tokens used, input tokens, and output tokens.
        /// </summary>
        [JsonPropertyName("usage")]
        public required UsageInfo Usage { get; set; }
}

public class ImageData
{
        /// <summary>
        /// The base64-encoded string representing the generated image.
        /// </summary>
        [JsonPropertyName("b64_json")]
        public required string B64Json { get; set; }
}

public class UsageInfo
{
        /// <summary>
        /// The total number of tokens used for the request.
        /// </summary>
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        /// <summary>
        /// The number of tokens used for the input of the request.
        /// </summary>
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        /// <summary>
        /// The number of tokens used for the output of the request.
        /// </summary>
        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }

        /// <summary>
        /// Detailed information about input tokens, including the breakdown into text and image tokens.
        /// </summary>
        [JsonPropertyName("input_tokens_details")]
        public required InputTokensDetails InputTokensDetails { get; set; }
}

public class InputTokensDetails
{
        /// <summary>
        /// The number of tokens used for text input.
        /// </summary>
        [JsonPropertyName("text_tokens")]
        public int TextTokens { get; set; }

        /// <summary>
        /// The number of tokens used for image input.
        /// </summary>
        [JsonPropertyName("image_tokens")]
        public int ImageTokens { get; set; }
}
