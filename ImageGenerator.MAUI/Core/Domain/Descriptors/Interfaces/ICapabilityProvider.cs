using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;

/// <summary>
/// Declares which UI knobs the model exposes (aspect ratios, GPT options, etc.).
/// Consumed by GeneratorViewModel to drive the per-model XAML show/hide flags.
/// </summary>
public interface ICapabilityProvider
{
    string ModelId { get; }
    ModelCapabilities Capabilities { get; }
}
