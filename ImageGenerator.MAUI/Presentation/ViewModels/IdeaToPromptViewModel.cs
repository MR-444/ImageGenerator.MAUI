using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Drives the "Describe an idea…" front door: a freeform idea → one Claude Opus 4.8 call (structured
/// output) → a schema-valid <see cref="V4JsonPrompt"/> → the same handoff the structure editor uses
/// (<c>Parameters.Prompt</c> + <c>UseJsonPrompt</c>). The VM never renders; it builds the caption and
/// drops it into the generator. Singleton so an in-progress idea + status survive a navigation
/// round-trip; the optional <see cref="GeneratorViewModel"/> lets tests construct it stand-alone.
/// </summary>
public partial class IdeaToPromptViewModel : ObservableObject
{
    private readonly IPromptBuilderService _builder;
    private readonly GeneratorViewModel? _generator;
    private readonly ILogger<IdeaToPromptViewModel> _logger;

    public IdeaToPromptViewModel(
        IPromptBuilderService builder,
        ILogger<IdeaToPromptViewModel> logger,
        GeneratorViewModel? generator = null)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _idea = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    private bool CanBuild() => !IsBusy && !string.IsNullOrWhiteSpace(Idea);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        IsBusy = true;
        SetStatus("Asking Claude to build a structured prompt… (Opus 4.8, may take a few seconds)", StatusKind.Info);
        try
        {
            var result = await _builder.BuildAsync(Idea);
            if (!result.Success || result.Prompt is null)
            {
                SetStatus(result.Error ?? "Couldn't build a prompt.", StatusKind.Error);
                return;
            }

            if (_generator is null)
            {
                // Stand-alone (tests): the prompt is built; there's nothing to hand off to.
                SetStatus("Prompt built.", StatusKind.Success);
                return;
            }

            ApplyToGenerator(result.Prompt);
            SetStatus("Structured prompt ready — opening the generator. Use “Edit structure…” to tweak.", StatusKind.Success);

            // Navigation is best-effort and isolated so a Shell failure can't undo the handoff above.
            try
            {
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IdeaToPrompt: navigation back failed");
            }
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

    /// <summary>The established handoff: serialize compact into the prompt box and flip on JSON mode —
    /// identical to <c>IdeogramStructureEditorViewModel.ApplyToGenerator</c>. Internal so tests assert it.</summary>
    internal void ApplyToGenerator(V4JsonPrompt prompt)
    {
        if (_generator is null) return;
        _generator.Parameters.Prompt = V4JsonPromptSerializer.Serialize(prompt);
        _generator.Parameters.UseJsonPrompt = true;
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }
}
