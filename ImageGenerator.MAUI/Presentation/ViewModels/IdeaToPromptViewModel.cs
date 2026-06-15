using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Drives the "Describe an idea…" front door as a two-pass flow. Pass 1 (always) turns the idea into a
/// normal <em>prose</em> prompt via <see cref="IPromptBuilderService.BuildProseAsync"/> — usable on its
/// own by every plain-prompt model and copyable for reuse. Pass 2 (optional, gated by the
/// <see cref="BuildJson"/> checkbox) maps that prose onto a schema-valid <see cref="V4JsonPrompt"/>. The
/// prose is never discarded: results stay on the page so the user can copy them, apply the prose as the
/// prompt, or apply the JSON. Handoff to the generator mirrors the structure editor
/// (<c>Parameters.Prompt</c> + <c>UseJsonPrompt</c>). Singleton so an in-progress idea + results survive
/// a navigation round-trip; the optional <see cref="GeneratorViewModel"/> lets tests build it stand-alone.
/// </summary>
public partial class IdeaToPromptViewModel : ObservableObject
{
    private readonly IPromptBuilderService _builder;
    private readonly IClipboardService _clipboard;
    private readonly GeneratorViewModel? _generator;
    private readonly ILogger<IdeaToPromptViewModel> _logger;

    private V4JsonPrompt? _builtJson;

    public IdeaToPromptViewModel(
        IPromptBuilderService builder,
        IClipboardService clipboard,
        ILogger<IdeaToPromptViewModel> logger,
        GeneratorViewModel? generator = null)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;

        // Default the JSON pass on for structured-JSON models (Ideogram V4 / ComfyUI), off otherwise.
        BuildJson = generator?.SupportsJsonPromptEditor ?? false;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _idea = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseJsonCommand))]
    private bool _isBusy;

    /// <summary>The checkbox: also run pass 2 (prose → Ideogram V4 JSON). Defaults from the model.</summary>
    [ObservableProperty]
    private bool _buildJson;

    /// <summary>The pass-1 prose prompt. Shown on the page, copyable, and applyable on its own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProse))]
    [NotifyCanExecuteChangedFor(nameof(CopyProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseProseCommand))]
    private string _prose = string.Empty;

    /// <summary>True once pass 2 has produced a JSON prompt — gates the "Use JSON prompt" button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseJsonCommand))]
    private bool _hasJson;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    /// <summary>Drives the result card's visibility — prose exists once a build has succeeded.</summary>
    public bool HasProse => !string.IsNullOrEmpty(Prose);

    private bool CanBuild() => !IsBusy && !string.IsNullOrWhiteSpace(Idea);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        IsBusy = true;
        // A fresh build invalidates any JSON from a previous run; keep the prose until it's replaced.
        HasJson = false;
        _builtJson = null;
        SetStatus("Asking Claude to write a prompt… (Opus 4.8, may take a few seconds)", StatusKind.Info);
        try
        {
            // Pass 1 (always): idea → prose.
            var proseResult = await _builder.BuildProseAsync(Idea);
            if (!proseResult.Success || proseResult.Prose is null)
            {
                SetStatus(proseResult.Error ?? "Couldn't build a prompt.", StatusKind.Error);
                return;
            }

            Prose = proseResult.Prose;

            if (!BuildJson)
            {
                SetStatus("Prose prompt ready — copy it or use it as your prompt.", StatusKind.Success);
                return;
            }

            // Pass 2 (optional): prose → Ideogram V4 JSON. On failure the prose above stays usable.
            SetStatus("Prose ready — building the Ideogram V4 JSON prompt…", StatusKind.Info);
            var jsonResult = await _builder.BuildJsonAsync(Prose);
            if (!jsonResult.Success || jsonResult.Prompt is null)
            {
                SetStatus(
                    (jsonResult.Error ?? "Couldn't build the JSON prompt.") + " The prose prompt above is still usable.",
                    StatusKind.Error);
                return;
            }

            _builtJson = jsonResult.Prompt;
            HasJson = true;
            SetStatus("Prose and Ideogram V4 JSON ready. Use either below.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeaToPrompt.{Op}", "Build");
            SetStatus($"Couldn't build the prompt: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseProse))]
    private async Task CopyProseAsync()
    {
        try
        {
            await _clipboard.SetTextAsync(Prose);
            SetStatus("Prose prompt copied to the clipboard.", StatusKind.Info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: copy prose failed");
            SetStatus("Couldn't copy to the clipboard.", StatusKind.Error);
        }
    }

    private bool CanUseProse() => !IsBusy && HasProse;

    [RelayCommand(CanExecute = nameof(CanUseProse))]
    private async Task UseProseAsync()
    {
        if (_generator is null)
        {
            // Stand-alone (tests): nothing to hand off to.
            SetStatus("Prose prompt ready.", StatusKind.Success);
            return;
        }

        _generator.Parameters.Prompt = Prose;
        _generator.Parameters.UseJsonPrompt = false;
        await NavigateBackAsync();
    }

    private bool CanUseJson() => !IsBusy && HasJson;

    [RelayCommand(CanExecute = nameof(CanUseJson))]
    private async Task UseJsonAsync()
    {
        if (_builtJson is null) return;

        if (_generator is null)
        {
            SetStatus("JSON prompt ready.", StatusKind.Success);
            return;
        }

        ApplyToGenerator(_builtJson);
        await NavigateBackAsync();
    }

    /// <summary>The established handoff: serialize compact into the prompt box and flip on JSON mode —
    /// identical to <c>IdeogramStructureEditorViewModel.ApplyToGenerator</c>. Internal so tests assert it.</summary>
    internal void ApplyToGenerator(V4JsonPrompt prompt)
    {
        if (_generator is null) return;
        _generator.Parameters.Prompt = V4JsonPromptSerializer.Serialize(prompt);
        _generator.Parameters.UseJsonPrompt = true;
    }

    // Navigation is best-effort and isolated so a Shell failure can't undo the handoff above.
    private async Task NavigateBackAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: navigation back failed");
        }
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }
}
