namespace ImageGenerator.MAUI.Shared.Constants;

// Display labels for the provider Picker. Centralized so the live-fetch path
// (ModelCatalogService.FormatProvider) and the hardcoded seed list emit the exact same
// string — the catalog merge dedupes by Value only, so divergent Provider strings would
// surface as duplicate provider entries in the picker.
//
// All Replicate-hosted models (Flux, GPT Image, Nano Banana, Ideogram, …) group under the
// single "Replicate" provider; the model creator is conveyed by the model's Display name.
public static class ProviderConstants
{
    public const string Replicate = "Replicate";
    public const string Pollinations = "Pollinations";
}
