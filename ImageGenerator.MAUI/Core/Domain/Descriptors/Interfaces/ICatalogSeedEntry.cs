using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;

/// <summary>
/// First-launch entry for the model picker — appears before Refresh Models hydrates from
/// the live Replicate catalog. The merge in GeneratorViewModel.ApplyCatalog dedupes by
/// ModelOption.Value, so live entries override seed entries on overlap.
/// </summary>
public interface ICatalogSeedEntry
{
    string ModelId { get; }
    ModelOption Seed { get; }
}
