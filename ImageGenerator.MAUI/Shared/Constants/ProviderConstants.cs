namespace ImageGenerator.MAUI.Shared.Constants;

// Display labels for the provider Picker. Centralized so the live-fetch path
// (ModelCatalogService.FormatProvider) and the hardcoded seed list in the VM emit the
// exact same string — the catalog merge dedupes by Value only, so divergent Provider
// strings would surface as duplicate provider entries in the picker.
public static class ProviderConstants
{
    public const string BlackForestLabs = "Black Forest Labs";
    public const string OpenAIOnReplicate = "OpenAI (via Replicate)";
    public const string Google = "Google";
}
