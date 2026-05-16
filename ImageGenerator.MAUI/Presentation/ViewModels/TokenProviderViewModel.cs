using CommunityToolkit.Mvvm.ComponentModel;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// One row in the API-tokens picker. Owns load / debounced-persist / forget for a single
/// provider, and forwards every value change to the host VM's parameters via the supplied
/// callback. Adding a third / fourth provider is a single new instance in the host's
/// TokenProviders collection — no XAML edits needed.
/// </summary>
public sealed partial class TokenProviderViewModel : ObservableObject
{
    public string Key { get; }
    public string DisplayName { get; }
    public string Placeholder { get; }
    public string HelperText { get; }
    public string ForgetButtonText => $"Forget {DisplayName} token";

    private readonly ITokenStore _store;
    private readonly Action<string> _syncToParameters;

    // Suspends OnValueChanged side-effects during programmatic writes (Load / Forget) so the
    // store isn't asked to persist what it just gave us back.
    private bool _suspendCallbacks;

    [ObservableProperty]
    private string _value = string.Empty;

    public TokenProviderViewModel(
        string key,
        string displayName,
        string placeholder,
        string helperText,
        ITokenStore store,
        Action<string> syncToParameters)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Placeholder = placeholder ?? throw new ArgumentNullException(nameof(placeholder));
        HelperText = helperText ?? throw new ArgumentNullException(nameof(helperText));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _syncToParameters = syncToParameters ?? throw new ArgumentNullException(nameof(syncToParameters));
    }

    partial void OnValueChanged(string value)
    {
        if (_suspendCallbacks) return;
        _syncToParameters(value);
        _store.Persist(value);
    }

    public async Task LoadAsync()
    {
        var saved = await _store.LoadAsync();
        if (string.IsNullOrEmpty(saved)) return;
        _suspendCallbacks = true;
        try
        {
            Value = saved;
            // Property setter is short-circuited above, so push to parameters explicitly.
            _syncToParameters(saved);
        }
        finally
        {
            _suspendCallbacks = false;
        }
    }

    public void Forget()
    {
        _store.Forget();
        _suspendCallbacks = true;
        try
        {
            Value = string.Empty;
            _syncToParameters(string.Empty);
        }
        finally
        {
            _suspendCallbacks = false;
        }
    }
}
