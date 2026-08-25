namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Read-only view of the generator's current input-image state, implemented by
/// <see cref="GeneratorViewModel"/> and consumed by the gallery detail page.
///
/// Exists so "Use as input" can refuse ON the detail page instead of navigating to MainPage
/// where nothing visibly happens: the Input Image card is hidden entirely for models without
/// <c>Capabilities.ImagePrompt</c>, so the coordinator's rejection status landed in a
/// one-line truncated label the user never saw.
/// </summary>
public interface IInputImageTarget
{
    /// <summary>True when the selected model takes a reference image at all.</summary>
    bool SupportsInputImage { get; }

    /// <summary>True when a further image fits under the selected model's cap.</summary>
    bool CanAddInputImage { get; }

    /// <summary>Compact "Provider · model" label, used in the refusal message.</summary>
    string SelectedModelSummary { get; }
}
