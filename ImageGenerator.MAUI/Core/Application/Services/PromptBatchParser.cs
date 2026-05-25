using System.Text;
using ImageGenerator.MAUI.Core.Application.Interfaces;

namespace ImageGenerator.MAUI.Core.Application.Services;

public sealed class PromptBatchParser : IPromptBatchParser
{
    private const string Delimiter = "---";
    private const char Bom = (char)0xFEFF;

    public IReadOnlyList<string> Parse(string fileContents)
    {
        if (string.IsNullOrEmpty(fileContents)) return [];

        // File.ReadAllTextAsync usually strips a UTF-8 BOM, but be defensive against
        // callers that hand us raw bytes decoded as a string.
        if (fileContents[0] == Bom) fileContents = fileContents[1..];

        var prompts = new List<string>();
        var current = new StringBuilder();

        foreach (var raw in fileContents.Split('\n'))
        {
            // Handle CRLF without rebuilding the whole string.
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            var trimmed = line.Trim();

            // A line that is blank, exactly "---", or a # comment all end the current prompt.
            // Blank lines and comment blocks are the natural separators in hand-written prompt
            // lists; "---" stays supported for forcing a split (or keeping a blank line inside
            // one prompt). Comment/blank text itself is never emitted.
            if (trimmed.Length == 0 || trimmed == Delimiter || trimmed.StartsWith('#'))
            {
                EmitIfNotEmpty(current, prompts);
                current.Clear();
                continue;
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }

        EmitIfNotEmpty(current, prompts);

        if (prompts.Count > PromptBatchParserLimits.MaxPromptsPerBatch)
            throw new PromptBatchTooLargeException(prompts.Count, PromptBatchParserLimits.MaxPromptsPerBatch);

        return prompts;
    }

    private static void EmitIfNotEmpty(StringBuilder buffer, List<string> sink)
    {
        var trimmed = buffer.ToString().Trim();
        if (trimmed.Length > 0) sink.Add(trimmed);
    }
}
