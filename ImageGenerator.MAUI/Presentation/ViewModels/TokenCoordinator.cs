using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// The API keys on SettingsPage: one row per provider, the selected row, Forget, and the CivitAI
/// connection test. SettingsPage binds it as Tokens.*.
///
/// It talks back through events, not delegates into its owner, so it can be built without one.
/// Only the Replicate and Pollinations tokens travel in the generation parameters; every other
/// service reads its store per request.
/// </summary>
public sealed partial class TokenCoordinator : ObservableObject
{
    private readonly ICivitaiPostingService _civitaiPostingService;
    private bool _loaded;

    public TokenCoordinator(
        IApiTokenStore replicateStore,
        IPollinationsTokenStore pollinationsStore,
        IComfyUiAuthStore comfyUiAuthStore,
        ICivitaiTokenStore civitaiStore,
        IAnthropicTokenStore anthropicStore,
        IOpenRouterTokenStore? openRouterStore,
        ICivitaiPostingService civitaiPostingService)
    {
        ArgumentNullException.ThrowIfNull(replicateStore);
        ArgumentNullException.ThrowIfNull(pollinationsStore);
        ArgumentNullException.ThrowIfNull(comfyUiAuthStore);
        ArgumentNullException.ThrowIfNull(civitaiStore);
        ArgumentNullException.ThrowIfNull(anthropicStore);
        ArgumentNullException.ThrowIfNull(civitaiPostingService);
        _civitaiPostingService = civitaiPostingService;

        // Display order of the Picker.
        Providers.Add(new TokenProviderViewModel(
            key: "replicate",
            displayName: "Replicate",
            placeholder: "Paste your Replicate API token…",
            helperText: "Required for Replicate-hosted models (Flux, GPT, Nano Banana). Stored in OS secure storage.",
            store: replicateStore,
            syncToParameters: v => ReplicateTokenChanged?.Invoke(v)));
        Providers.Add(new TokenProviderViewModel(
            key: "pollinations",
            displayName: "Pollinations",
            placeholder: "Paste your Pollinations token (optional)…",
            helperText: "Optional. Anonymous mode is rate-limited (1 req / 15 s). Register at auth.pollinations.ai for higher limits.",
            store: pollinationsStore,
            syncToParameters: v => PollinationsTokenChanged?.Invoke(v)));
        Providers.Add(new TokenProviderViewModel(
            key: "comfyui",
            displayName: "ComfyUI",
            placeholder: "Authorization header value (e.g. Bearer abc123)…",
            helperText: "Optional. Sent verbatim as the Authorization header on every ComfyUI request "
                        + "(HTTP + progress WebSocket) — for reverse-proxied servers. Leave empty on a LAN.",
            store: comfyUiAuthStore,
            syncToParameters: static _ => { }));
        Providers.Add(new TokenProviderViewModel(
            key: "civitai",
            displayName: "CivitAI",
            placeholder: "Paste your CivitAI API key…",
            helperText: "Optional. Needed only for \"Post to CivitAI\". Create a Full API key at "
                        + "civitai.com/user/account (a free account works). Stored in OS secure storage.",
            store: civitaiStore,
            syncToParameters: static _ => { }));
        Providers.Add(new TokenProviderViewModel(
            key: "anthropic",
            displayName: "Anthropic",
            placeholder: "Paste your Anthropic API key…",
            helperText: "Required when \"Describe an idea…\" or AI tools use a Claude tier. Create a key at "
                        + "console.anthropic.com. Stored in OS secure storage.",
            store: anthropicStore,
            syncToParameters: static _ => { }));
        if (openRouterStore is not null)
        {
            Providers.Add(new TokenProviderViewModel(
                key: "openrouter",
                displayName: "OpenRouter",
                placeholder: "Paste your OpenRouter API key…",
                helperText: "Required when image observation uses OpenRouter. The model id is configured below.",
                store: openRouterStore,
                syncToParameters: static _ => { }));
        }
        _selectedProvider = Providers[0];
    }

    /// <summary>Raised with the Replicate token whenever its row loads, changes or is forgotten.</summary>
    public event Action<string>? ReplicateTokenChanged;

    /// <summary>Raised with the Pollinations token whenever its row loads, changes or is forgotten.</summary>
    public event Action<string>? PollinationsTokenChanged;

    /// <summary>A message for the owner's status line.</summary>
    public event Action<string, StatusKind>? StatusRaised;

    // One token Entry per item; adding a provider needs no XAML change.
    public ObservableCollection<TokenProviderViewModel> Providers { get; } = [];

    [ObservableProperty]
    private TokenProviderViewModel? _selectedProvider;

    // Null hides the result label.
    [ObservableProperty]
    private string? _civitaiConnectionStatus;

    /// <summary>Loads every provider token from secure storage, once.</summary>
    public async Task LoadAllAsync()
    {
        if (_loaded) return;
        _loaded = true;

        foreach (var provider in Providers)
        {
            await provider.LoadAsync();
        }
    }

    /// <summary>
    /// Raises the token events again for every row's current value, without touching storage.
    /// The owner calls this after replacing its parameters object, which arrives with empty slots.
    /// </summary>
    public void PushAll()
    {
        foreach (var provider in Providers) provider.PushToParameters();
    }

    [RelayCommand]
    private void ForgetSelected()
    {
        if (SelectedProvider is null) return;
        var name = SelectedProvider.DisplayName;
        SelectedProvider.Forget();
        StatusRaised?.Invoke($"{name} token cleared from secure storage.", StatusKind.Info);
    }

    [RelayCommand]
    private async Task TestCivitaiConnectionAsync()
    {
        CivitaiConnectionStatus = "Testing…";
        var result = await _civitaiPostingService.TestConnectionAsync();
        CivitaiConnectionStatus = result.Message;
    }
}
