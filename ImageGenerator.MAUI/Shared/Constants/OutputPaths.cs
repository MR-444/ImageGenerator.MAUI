namespace ImageGenerator.MAUI.Shared.Constants;

public static class OutputPaths
{
    public const string FolderName = "Emberforge";

    // User-chosen override for the ROOT data folder (the "Configurable output folder"
    // setting). null => the default Pictures location below. Every data folder — images,
    // json-prompts, comfy-workflows, mutation-library, prompt-builder — derives from this
    // root, so pointing it at a new drive moves the whole app together. Set once at the
    // composition root from the persisted preference, and live whenever the user changes it
    // in Settings. Owned here as a plain string so this layer stays free of any MAUI/storage
    // dependency — the preference KEY lives in UiStateStore.
    private static string? _rootOverride;

    // The fixed Pictures\Emberforge location: easy to find and survives app rebuilds. This is
    // the default root when no override is set, and stays put regardless of the setting — it's
    // where app.log is anchored (configured pre-DI, before the override is loaded) so the log
    // never moves to a user-chosen (possibly flaky/network) drive.
    public static string DefaultRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), FolderName);

    // The active root: the configured output folder when set, otherwise the default.
    public static string RootDirectory => _rootOverride ?? DefaultRootDirectory;

    // Per-user, machine-local diagnostics home (app.log). The Windows-standard %LOCALAPPDATA%
    // location — deliberately NOT under the data root: logs are diagnostics, not user content,
    // and must never sit in Pictures or move to a (possibly flaky) configured output drive.
    // Pure BCL (no MAUI FileSystem dependency) so it's valid in the pre-DI CrashLogger path.
    public static string DiagnosticsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    // Apply (or clear) the configured root folder. null/whitespace restores the default.
    public static void SetRootOverride(string? path) =>
        _rootOverride = string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    // Where generated images land: a "pictures" subfolder of the active root.
    public static string GeneratedImagesDirectory =>
        Path.Combine(RootDirectory, "pictures");

    // Exported Ideogram structured prompts (pretty-printed .json, portable to the official
    // API / a local Ideogram install). A sibling of the images folder under the root.
    public static string JsonPromptsDirectory =>
        Path.Combine(RootDirectory, "json-prompts");

    // ComfyUI workflow templates the user exports via Workflow > Export (API). Each file's
    // stem becomes a "comfyui/<stem>" model in the picker. Lives under the active root so the
    // whole app stays together when the output folder is re-pointed — drop workflows in
    // <root>\comfy-workflows and they appear in the picker.
    public static string ComfyWorkflowsDirectory =>
        Path.Combine(RootDirectory, "comfy-workflows");

    // User-editable mutation library (style fragments, ornament kits, scene-element templates)
    // the caption mutation engine draws from, seeded from bundled defaults on first use. Lives
    // under the active root alongside the other data folders.
    public static string MutationLibraryDirectory =>
        Path.Combine(RootDirectory, "mutation-library");

    // Local, non-repo home for the "Describe an idea…" prompt builder's private override. Drop a
    // "system-prompt.md" here to replace the bundled clean-room builder prompt with a private one
    // (3-yr prompt IP that must never enter the public repo — open-core split). Lives under the
    // active root alongside the other data folders.
    public static string PromptBuilderDirectory =>
        Path.Combine(RootDirectory, "prompt-builder");
}
