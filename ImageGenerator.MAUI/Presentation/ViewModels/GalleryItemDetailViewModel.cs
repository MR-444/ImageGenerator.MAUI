using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GalleryItemDetailViewModel : ObservableObject
{
    private readonly IGalleryService _galleryService;
    private readonly IFileLauncher _fileLauncher;
    private readonly IClipboardService _clipboard;
    private readonly IMutationLibraryService _libraryService;
    private readonly ILogger<GalleryItemDetailViewModel> _logger;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _fileName;

    // Single-line summary under the filename: e.g. "1.4 MB · 2026-05-04 17:43".
    [ObservableProperty]
    private string? _summary;

    // Read-only multi-line text bound to a selectable Editor on the page so the user can
    // copy any subset by hand even without using the dedicated Copy button.
    [ObservableProperty]
    private string? _metadataText;

    [ObservableProperty]
    private bool _isBusy;

    // Transient feedback shown briefly under the action row (e.g. after Copy). Cleared by
    // FlashAsync after a short delay so the user gets confirmation without a modal dialog.
    [ObservableProperty]
    private string? _flashMessage;

    public GalleryItemDetailViewModel(
        IGalleryService galleryService,
        IFileLauncher fileLauncher,
        IClipboardService clipboard,
        IMutationLibraryService libraryService,
        ILogger<GalleryItemDetailViewModel> logger)
    {
        _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
        _fileLauncher = fileLauncher ?? throw new ArgumentNullException(nameof(fileLauncher));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called by the page after the navigation QueryProperty has populated FilePath.
    /// Loads metadata + computes the summary line.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        IsBusy = true;
        try
        {
            FileName = Path.GetFileName(FilePath);
            Summary = BuildSummary(FilePath);

            var meta = await _galleryService.ReadMetadataAsync(FilePath, ct);
            MetadataText = FormatMetadata(meta);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "Load");
            MetadataText = "Couldn't read metadata. See app.log for details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyMetadataAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(MetadataText)) return;
            await _clipboard.SetTextAsync(MetadataText);
            _ = FlashAsync("Copied to clipboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "CopyMetadata");
        }
    }

    private async Task FlashAsync(string message, int durationMs = 2000)
    {
        FlashMessage = message;
        await Task.Delay(durationMs);
        // Don't clobber a flash that a later action set in the meantime.
        if (FlashMessage == message) FlashMessage = null;
    }

    [RelayCommand]
    private void OpenInViewer()
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            _fileLauncher.Launch(FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "OpenInViewer");
        }
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            _fileLauncher.RevealInFolder(FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "ShowInFolder");
        }
    }

    [RelayCommand]
    private async Task RemixAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            // Hand the path off to MainPage's "remixFrom" QueryProperty, which loads this image's
            // embedded recipe (prompt, model, seed, params) back into the generator. The absolute
            // "//MainPage" form pops the gallery + detail off the stack so the user lands on the
            // generator with the recipe applied. ShellNavigationQueryParameters is single-use: a
            // string query suffix would be RE-applied on every later back navigation to MainPage,
            // re-loading the recipe and stomping any edits made since.
            await Shell.Current.GoToAsync("//MainPage",
                new ShellNavigationQueryParameters { ["remixFrom"] = FilePath });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "Remix");
        }
    }

    [RelayCommand]
    private async Task MutateFromImageAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            // Hand the path to MainPage's "mutateFrom" QueryProperty, which restores this image's
            // recipe and opens the mutation engine seeded with its structured caption. Same
            // single-use ShellNavigationQueryParameters rationale as Remix: a string query suffix
            // would be re-applied on every later back navigation to MainPage.
            await Shell.Current.GoToAsync("//MainPage",
                new ShellNavigationQueryParameters { ["mutateFrom"] = FilePath });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "MutateFromImage");
        }
    }

    /// <summary>
    /// Capture this image's structured-caption style block as a named <see cref="StyleFragment"/> in the
    /// mutation library, so the deterministic engine can re-use a look the AI invented. The name is
    /// prompted by the page (a View concern) and passed in, keeping this unit-testable. Each failure path
    /// flashes a message and returns; an existing name is rejected (never clobbered).
    /// </summary>
    internal async Task SaveStyleAsync(string name, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath)) return;

            var styleName = name?.Trim() ?? string.Empty;
            if (styleName.Length == 0)
            {
                _ = FlashAsync("Enter a name for the style.");
                return;
            }

            var meta = await _galleryService.ReadMetadataAsync(FilePath, ct);
            if (meta is null || !meta.TryGetValue("Prompt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
            {
                _ = FlashAsync("This image has no structured caption to save a style from.");
                return;
            }

            V4JsonPrompt parsed;
            try
            {
                parsed = V4JsonPromptSerializer.Deserialize(prompt);
            }
            catch (V4JsonPromptParseException)
            {
                _ = FlashAsync("This image's caption isn't a structured prompt.");
                return;
            }

            if (parsed.StyleDescription is null)
            {
                _ = FlashAsync("This caption has no style block to save.");
                return;
            }

            var library = await _libraryService.LoadAsync(ct);
            if (library.FragmentByName(styleName) is not null)
            {
                _ = FlashAsync($"Style '{styleName}' already exists — pick another name.");
                return;
            }

            var updated = new MutationLibrary(
                [.. library.StyleFragments, new StyleFragment(styleName, parsed.StyleDescription)],
                library.OrnamentKits,
                library.SceneElements,
                library.AnchorPresets);
            await _libraryService.SaveAsync(updated, ct);

            _ = FlashAsync($"Saved style '{styleName}' to the mutation library.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GalleryItemDetailVM.{Op}", "SaveStyle");
            _ = FlashAsync("Couldn't save the style. See app.log for details.");
        }
    }

    [RelayCommand]
    private async Task UseAsInputAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            // Hand the path off to MainPage's QueryProperty. The absolute "//MainPage" form
            // pops the gallery + detail off the stack so the user lands directly on the
            // generator with the image already attached. ShellNavigationQueryParameters is
            // single-use: a string query suffix would be RE-applied on every later back
            // navigation to MainPage, silently re-adding a removed input image.
            await Shell.Current.GoToAsync("//MainPage",
                new ShellNavigationQueryParameters { ["addInput"] = FilePath });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "UseAsInput");
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        // The window's X closes the whole app on Windows; this is the in-page back action.
        // ".." pops one level, returning to the gallery rather than the main page.
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryItemDetailVM.{Op}", "Close");
        }
    }

    private static string BuildSummary(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return $"{FormatBytes(info.Length)} · {info.CreationTime:yyyy-MM-dd HH:mm}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatMetadata(IReadOnlyDictionary<string, string>? meta)
    {
        if (meta is null || meta.Count == 0) return "No embedded metadata found.";

        // Header rows the user typically cares about first; everything else falls under
        // alphabetical for predictability.
        var preferredOrder = new[] { "Prompt", "ModelName", "Seed", "AspectRatio", "Dimensions", "Format", "Quality" };
        var ordered = meta
            .OrderBy(kv => Array.IndexOf(preferredOrder, kv.Key) is var idx && idx >= 0 ? idx : int.MaxValue)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal);

        return string.Join(Environment.NewLine, ordered.Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }
}
