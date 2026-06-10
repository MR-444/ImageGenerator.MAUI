using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class JsonPromptFileService : IJsonPromptFileService
{
    private readonly string _directory;
    private readonly Func<DateTime> _clock;

    // Directory + clock injectable for tests (temp dir, frozen timestamp) — same seam style
    // as ImageFileService's clock. MS.DI uses the defaults since neither is registered.
    public JsonPromptFileService(string? directory = null, Func<DateTime>? clock = null)
    {
        _directory = directory ?? OutputPaths.JsonPromptsDirectory;
        _clock = clock ?? (() => DateTime.Now);
    }

    public async Task<string> SaveAsync(string descriptionForName, string prettyJson, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);

        var path = GetUniqueSavePath(BuildFileName(descriptionForName));
        await File.WriteAllTextAsync(path, prettyJson, ct);
        return path;
    }

    private string BuildFileName(string description)
    {
        var timestamp = _clock().ToString("yyyyMMdd_HHmmss");
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(description.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray())
            .Replace(" ", "_")
            .Replace("__", "_");
        safeName = safeName.Length > 30 ? safeName[..30] : safeName;
        // Whitespace-only input degrades to underscores above, not to empty — strip them too.
        if (string.IsNullOrWhiteSpace(safeName.Trim('_'))) safeName = "structured-prompt";
        return $"{timestamp}_{safeName}.json";
    }

    private string GetUniqueSavePath(string baseName)
    {
        var candidate = Path.Combine(_directory, baseName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(baseName);
        for (var i = 1; i < 100; i++)
        {
            var next = Path.Combine(_directory, $"{stem}_{i}.json");
            if (!File.Exists(next)) return next;
        }
        throw new IOException($"Could not find an unused filename for '{baseName}' in '{_directory}' after 100 attempts.");
    }
}
