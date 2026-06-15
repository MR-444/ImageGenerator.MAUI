using System.Text.Json;

namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>
/// The JSON Schema mirroring <c>V4JsonPrompt</c>'s SHAPE, shared by every Anthropic/OpenAI-compatible
/// structured-output call that asks a model for a V4 caption (the prompt builder's JSON pass and the
/// caption mutator). Structured outputs cannot express string length, numeric ranges, regex, or the
/// art_style XOR photo rule — those stay in <c>V4JsonPromptValidator</c>. <c>additionalProperties:false</c>
/// on every object is required by structured outputs.
/// </summary>
internal static class V4StructuredSchema
{
    /// <summary>Parsed once; callers pass this straight into the request body's json_schema format.</summary>
    public static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "high_level_description": { "type": "string" },
            "style_description": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "aesthetics": { "type": "string" },
                "lighting": { "type": "string" },
                "medium": { "type": "string" },
                "art_style": { "type": "string" },
                "photo": { "type": "string" },
                "color_palette": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["medium"]
            },
            "compositional_deconstruction": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "background": { "type": "string" },
                "elements": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "type": { "type": "string", "enum": ["obj", "text"] },
                      "bbox": { "type": "array", "items": { "type": "integer" } },
                      "text": { "type": "string" },
                      "desc": { "type": "string" },
                      "color_palette": { "type": "array", "items": { "type": "string" } }
                    },
                    "required": ["type", "desc"]
                  }
                }
              },
              "required": ["background", "elements"]
            }
          },
          "required": ["high_level_description", "compositional_deconstruction"]
        }
        """).RootElement.Clone();
}
