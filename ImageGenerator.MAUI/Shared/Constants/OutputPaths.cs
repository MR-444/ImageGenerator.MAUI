namespace ImageGenerator.MAUI.Shared.Constants;

public static class OutputPaths
{
    public const string FolderName = "ImageGenerator.MAUI";

    // User-chosen override for where generated images are saved (the "Configurable output
    // folder" setting). null => the default Pictures location below. Set once at the
    // composition root from the persisted preference, and live whenever the user changes it
    // in Settings. Owned here as a plain string so this layer stays free of any MAUI/storage
    // dependency — the preference KEY lives in UiStateStore.
    private static string? _generatedImagesOverride;

    // The fixed Pictures\ImageGenerator.MAUI location: easy to find and survives app rebuilds.
    // This stays put regardless of the output-folder setting — app.log and the ComfyUI
    // workflow templates are anchored here so they never disappear when the output moves.
    public static string DefaultGeneratedImagesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), FolderName);

    // Where generated images land. Follows the user's configured output folder when set.
    public static string GeneratedImagesDirectory =>
        _generatedImagesOverride ?? DefaultGeneratedImagesDirectory;

    // Apply (or clear) the configured output folder. null/whitespace restores the default.
    public static void SetGeneratedImagesOverride(string? path) =>
        _generatedImagesOverride = string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    // Exported Ideogram structured prompts (pretty-printed .json, portable to the official
    // API / a local Ideogram install). Lives next to the images — they're generation outputs
    // too — so this follows the configured output folder.
    public static string JsonPromptsDirectory =>
        Path.Combine(GeneratedImagesDirectory, "json-prompts");

    // ComfyUI workflow templates the user exports via Workflow > Export (API). Each file's
    // stem becomes a "comfyui/<stem>" model in the picker. These are INPUTS (picker models),
    // not outputs, so they stay anchored at the default location — moving the output folder
    // must never make the user's workflow models vanish from the picker.
    public static string ComfyWorkflowsDirectory =>
        Path.Combine(DefaultGeneratedImagesDirectory, "comfy-workflows");

    // User-editable mutation library (style fragments, ornament kits, scene-element templates)
    // the caption mutation engine draws from, seeded from bundled defaults on first use. Like the
    // ComfyUI workflows above, this is hand-edited INPUT, not output, so it stays anchored at the
    // default location — re-pointing the output folder must never strand the user's edited library.
    public static string MutationLibraryDirectory =>
        Path.Combine(DefaultGeneratedImagesDirectory, "mutation-library");
}
