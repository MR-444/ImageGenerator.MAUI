using System.Text.Json;
using System.Text.Json.Nodes;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;

namespace ImageGenerator.MAUI.Core.Domain.ComfyUi;

/// <summary>What the patcher changed — the service logs it, tests pin it.</summary>
public sealed record ComfyUiPatchResult(
    string GraphJson,
    string PromptTargetDescription,
    IReadOnlyList<string> SeedNodeIds);

/// <summary>
/// Pure transform from an API-format ComfyUI workflow export to the graph actually submitted:
/// injects the prompt, re-rolls the seeds, and applies aspect ratio / megapixels.
/// <para>
/// API format is a top-level map of node-id → {class_type, inputs}; an input value that is a
/// JSON array is a LINK to another node's output. The prompt injection deliberately overwrites
/// links (that is how the app's JSON takes over a wired import_json); everything else only
/// touches literal values. Throws <see cref="InvalidOperationException"/> with user-facing
/// messages on unpatchable templates — the service surfaces them as the job message.
/// </para>
/// </summary>
public static class ComfyUiWorkflowPatcher
{
    private const string PromptBuilderClass = "Ideogram4PromptBuilderKJ";
    private const string TextEncodeClass = "CLIPTextEncode";
    private const string ResolutionSelectorClass = "ResolutionSelector";

    public static ComfyUiPatchResult Patch(string templateJson, ComfyUiRequest request, DateTimeOffset? now = null)
    {
        if (JsonNode.Parse(templateJson) is not JsonObject root)
            throw new InvalidOperationException("The workflow file does not contain a JSON object.");

        // A UI-format save (normal Ctrl+S / server-side save) carries a top-level "nodes"
        // ARRAY; only the API export can be queued. Conversion is frontend-only — never try.
        if (root["nodes"] is JsonArray)
            throw new InvalidOperationException(
                "This file is a UI-format workflow save. Re-export it from ComfyUI via "
                + "Workflow > Export (API) and replace the file.");

        var nodes = CollectNodes(root);
        if (nodes.Count == 0)
            throw new InvalidOperationException("The workflow contains no nodes.");

        var promptTarget = request.UseJsonPrompt
            ? PatchJsonPrompt(nodes, request.Prompt)
            : PatchPlainPrompt(nodes, request.Prompt);

        var seedNodeIds = PatchSeeds(nodes, request.Seed);
        var resolutionNote = PatchResolution(nodes, request.AspectRatio, request.Megapixels);
        var dateNote = PatchFilenamePrefixDates(nodes, now ?? DateTimeOffset.Now);

        return new ComfyUiPatchResult(
            root.ToJsonString(),
            promptTarget + resolutionNote + dateNote,
            seedNodeIds);
    }

    private static List<(string Id, string ClassType, JsonObject Inputs)> CollectNodes(JsonObject root)
    {
        var nodes = new List<(string, string, JsonObject)>();
        foreach (var (id, value) in root)
        {
            if (value is JsonObject node
                && node["class_type"]?.GetValueKind() == JsonValueKind.String
                && node["inputs"] is JsonObject inputs)
            {
                nodes.Add((id, node["class_type"]!.GetValue<string>(), inputs));
            }
        }
        // Deterministic "first node" rule: numeric ids ascending, non-numeric after, ordinal.
        return nodes
            .OrderBy(n => int.TryParse(n.Item1, out var numeric) ? numeric : int.MaxValue)
            .ThenBy(n => n.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private static string PatchJsonPrompt(
        List<(string Id, string ClassType, JsonObject Inputs)> nodes, string prompt)
    {
        var builders = nodes.Where(n => n.ClassType == PromptBuilderClass).ToList();
        foreach (var (_, _, inputs) in builders)
        {
            // Overwrites a wired link if present — the whole point is that the app's JSON
            // becomes authoritative ("always" beats the node's own editor state).
            inputs["import_json"] = prompt;
            inputs["import_mode"] = "always";
        }

        // The builder can be wired as a mere VIEWER (its prompt output feeding only a preview)
        // while the conditioning comes from a CLIPTextEncode whose text is a frozen caption-
        // JSON literal — the user's workflow_MR export has exactly that shape. Replace every
        // such JSON literal too; non-JSON literals (plain/negative prompts) stay untouched.
        var jsonLiteralEncodes = nodes
            .Where(n => n.ClassType == TextEncodeClass
                        && n.Inputs["text"]?.GetValueKind() == JsonValueKind.String
                        && LooksLikeJsonObject(n.Inputs["text"]!.GetValue<string>()))
            .ToList();
        foreach (var (_, _, inputs) in jsonLiteralEncodes)
        {
            inputs["text"] = prompt;
        }

        if (builders.Count == 0 && jsonLiteralEncodes.Count == 0)
            throw new InvalidOperationException(
                $"This workflow has no {PromptBuilderClass} node and no caption-JSON "
                + $"{TextEncodeClass} literal to receive the structured JSON — uncheck "
                + "'Structured JSON prompt' or pick an Ideogram workflow.");

        return $"import_json on {builders.Count} {PromptBuilderClass} node(s)"
               + $" + text on {jsonLiteralEncodes.Count} JSON-literal {TextEncodeClass} node(s)";
    }

    private static bool LooksLikeJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static string PatchPlainPrompt(
        List<(string Id, string ClassType, JsonObject Inputs)> nodes, string prompt)
    {
        // Lowest-id CLIPTextEncode with a LITERAL text wins: deterministic, hits the positive
        // prompt in stock graphs, and never hijacks a link-driven encoder.
        var target = nodes.FirstOrDefault(n =>
            n.ClassType == TextEncodeClass
            && n.Inputs["text"]?.GetValueKind() == JsonValueKind.String);
        if (target.Inputs is null)
            throw new InvalidOperationException(
                "No editable text input found — this workflow's prompt is driven by a builder "
                + "node; check 'Structured JSON prompt' instead.");

        target.Inputs["text"] = prompt;
        return $"text on {TextEncodeClass} node {target.Id}";
    }

    private static List<string> PatchSeeds(
        List<(string Id, string ClassType, JsonObject Inputs)> nodes, long seed)
    {
        // ComfyUI's "randomize after generate" lives in the frontend; an API submission reuses
        // whatever literal the export froze, yielding the identical image every run. Re-roll
        // every literal numeric seed-like input; links and strings stay untouched.
        var patched = new List<string>();
        foreach (var (id, _, inputs) in nodes)
        {
            var touched = false;
            foreach (var key in new[] { "seed", "noise_seed" })
            {
                if (inputs[key]?.GetValueKind() == JsonValueKind.Number)
                {
                    inputs[key] = seed;
                    touched = true;
                }
            }
            if (touched) patched.Add(id);
        }
        return patched;
    }

    private static string PatchResolution(
        List<(string Id, string ClassType, JsonObject Inputs)> nodes,
        string? aspectRatio, double? megapixels)
    {
        if (aspectRatio is null && megapixels is null) return string.Empty;

        var selectors = nodes.Where(n => n.ClassType == ResolutionSelectorClass).ToList();
        if (selectors.Count == 0)
            return "; no ResolutionSelector node — workflow keeps its own resolution";

        foreach (var (_, _, inputs) in selectors)
        {
            if (aspectRatio is not null) inputs["aspect_ratio"] = aspectRatio;
            if (megapixels is not null) inputs["megapixels"] = megapixels;
        }
        return $"; resolution on {selectors.Count} ResolutionSelector node(s)";
    }

    private static string PatchFilenamePrefixDates(
        List<(string Id, string ClassType, JsonObject Inputs)> nodes, DateTimeOffset now)
    {
        // ComfyUI's %date:FORMAT% tokens are expanded by the BROWSER frontend at queue time;
        // the server takes filename_prefix literally, and the ':' inside an unexpanded token
        // is path-invalid on Windows (SaveImage fails with WinError 267). Expand them here so
        // raw API exports keep their dated server subfolders when queued by the app.
        var expanded = 0;
        foreach (var (_, _, inputs) in nodes)
        {
            if (inputs["filename_prefix"]?.GetValueKind() != JsonValueKind.String) continue;

            var text = inputs["filename_prefix"]!.GetValue<string>();
            var replaced = ExpandDateTokens(text, now);
            if (replaced != text)
            {
                inputs["filename_prefix"] = replaced;
                expanded++;
            }
        }
        return expanded == 0 ? string.Empty : $"; %date% expanded on {expanded} filename_prefix input(s)";
    }

    private static string ExpandDateTokens(string text, DateTimeOffset now)
    {
        var start = text.IndexOf("%date", StringComparison.Ordinal);
        if (start < 0) return text;

        var result = new System.Text.StringBuilder(text.Length);
        var pos = 0;
        while (pos < text.Length)
        {
            var tokenStart = text.IndexOf("%date", pos, StringComparison.Ordinal);
            if (tokenStart < 0)
            {
                result.Append(text, pos, text.Length - pos);
                break;
            }
            result.Append(text, pos, tokenStart - pos);

            var afterDate = tokenStart + "%date".Length;
            if (afterDate < text.Length && text[afterDate] == '%')
            {
                // Bare %date% — the frontend's default format.
                result.Append(FormatDateSpec("yyyyMMddhhmmss", now));
                pos = afterDate + 1;
            }
            else if (afterDate < text.Length && text[afterDate] == ':'
                     && text.IndexOf('%', afterDate + 1) is var specEnd && specEnd >= 0)
            {
                result.Append(FormatDateSpec(text[(afterDate + 1)..specEnd], now));
                pos = specEnd + 1;
            }
            else
            {
                // Unterminated token — leave verbatim rather than guess.
                result.Append(text, tokenStart, text.Length - tokenStart);
                pos = text.Length;
            }
        }
        return result.ToString();
    }

    private static string FormatDateSpec(string spec, DateTimeOffset now)
    {
        // The frontend's format chars; "hh" is 24-hour. Positional scan, longest token first,
        // so already-substituted digits can never be re-matched (unlike sequential Replace).
        var result = new System.Text.StringBuilder(spec.Length + 8);
        var tokens = DateTokens(now);
        var pos = 0;
        while (pos < spec.Length)
        {
            var matched = false;
            foreach (var (token, value) in tokens)
            {
                if (string.CompareOrdinal(spec, pos, token, 0, token.Length) == 0)
                {
                    result.Append(value);
                    pos += token.Length;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                result.Append(spec[pos]);
                pos++;
            }
        }
        return result.ToString();
    }

    private static (string Token, string Value)[] DateTokens(DateTimeOffset now) =>
    [
        ("yyyy", now.Year.ToString("D4")),
        ("yy", (now.Year % 100).ToString("D2")),
        ("MM", now.Month.ToString("D2")),
        ("M", now.Month.ToString()),
        ("dd", now.Day.ToString("D2")),
        ("d", now.Day.ToString()),
        ("hh", now.Hour.ToString("D2")),
        ("h", now.Hour.ToString()),
        ("mm", now.Minute.ToString("D2")),
        ("m", now.Minute.ToString()),
        ("ss", now.Second.ToString("D2")),
        ("s", now.Second.ToString()),
    ];
}
