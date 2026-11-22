using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// The generator's routes to the companion pages (mutation engine, prompt builder, structure
/// editor) and the hand-off slots those pages read once on appearing. Created and held by
/// GeneratorViewModel; MainPage binds it as Navigation.*, the sibling pages reach the slots
/// through <c>Generator.Navigation</c>.
/// </summary>
public sealed partial class NavigationCoordinator
{
    private readonly Func<ImageGenerationParameters> _parametersAccessor;
    private readonly IGalleryService _galleryService;
    private readonly Action<string, StatusKind> _setStatus;
    private readonly Func<string, Task> _navigate;
    private readonly ILogger _logger;

    /// <param name="navigate">Goes to a Shell route; Shell.Current by default. Tests pass their own.</param>
    public NavigationCoordinator(
        Func<ImageGenerationParameters> parametersAccessor,
        IGalleryService galleryService,
        Action<string, StatusKind> setStatus,
        ILogger logger,
        Func<string, Task>? navigate = null)
    {
        _parametersAccessor = parametersAccessor ?? throw new ArgumentNullException(nameof(parametersAccessor));
        _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigate = navigate ?? (route => Shell.Current.GoToAsync(route));
    }

    // Read on every access: the owner's parameters instance is replaceable.
    private ImageGenerationParameters Parameters => _parametersAccessor();

    /// <summary>
    /// Hand-off to the mutation page, read once on its appearance. A typed model, because a
    /// string round-trip would strip explicit <c>Element.SlotTag</c> values.
    /// </summary>
    public V4JsonPrompt? PendingMutationBase { get; set; }

    /// <summary>Hand-off from the Gallery's breed action to the mutation page, read once.</summary>
    public IReadOnlyList<V4JsonPrompt>? PendingBreedSet { get; set; }

    /// <summary>
    /// Seeds the mutation page from the prompt box. A non-structured prompt leaves
    /// <see cref="PendingMutationBase"/> null and the mutation page reports it.
    /// </summary>
    [RelayCommand]
    private Task OpenMutationEngineAsync()
    {
        PendingMutationBase = TryParse(Parameters.Prompt);
        return NavigateAsync("mutation-engine", "the mutation engine");
    }

    /// <summary>
    /// "Mutate from this" on a saved image: adopts only its caption as the mutation base and
    /// keeps the current model and settings, so there is no surprise model or resolution switch.
    /// A non-structured caption leaves <see cref="PendingMutationBase"/> null.
    /// </summary>
    public async Task MutateFromImageAsync(string? filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var meta = await _galleryService.ReadMetadataAsync(filePath, ct);
            if (meta is null || !meta.TryGetValue("Prompt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
            {
                _setStatus("No caption found in this image.", StatusKind.Warning);
                return;
            }

            Parameters.Prompt = prompt;
            PendingMutationBase = TryParse(prompt);

            await NavigateAsync("mutation-engine", "the mutation engine");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mutate from image failed for {Path}", filePath);
            _setStatus("Couldn't load this image's caption. See app.log for details.", StatusKind.Error);
        }
    }

    [RelayCommand]
    private Task OpenIdeaToPromptAsync() => NavigateAsync("idea-to-prompt", "the prompt builder");

    [RelayCommand]
    private Task OpenIdeogramEditorAsync() => NavigateAsync(BuildEditorRoute(), "the structure editor");

    /// <summary>
    /// The editor route, carrying the prompt and the output shape. On ComfyUI the shape lives in
    /// Parameters.AspectRatio (a ResolutionSelector combo string), elsewhere in Resolution.
    /// </summary>
    internal string BuildEditorRoute()
    {
        var json = Uri.EscapeDataString(Parameters.Prompt);
        var shape = ModelConstants.ComfyUi.IsId(Parameters.Model)
            ? Parameters.AspectRatio
            : Parameters.Resolution;
        return $"ideogram-editor?json={json}&resolution={Uri.EscapeDataString(shape)}";
    }

    private static V4JsonPrompt? TryParse(string prompt)
    {
        try { return V4JsonPromptSerializer.Deserialize(prompt); }
        catch (V4JsonPromptParseException) { return null; }
    }

    /// <summary>
    /// Navigates and reports a failure on the status line. <paramref name="target"/> is the noun
    /// in "Couldn't open …".
    /// </summary>
    private async Task NavigateAsync(string route, string target)
    {
        try
        {
            await _navigate(route);
        }
        catch (Exception ex)
        {
            _setStatus($"Couldn't open {target}: {ex.Message}", StatusKind.Error);
        }
    }
}
