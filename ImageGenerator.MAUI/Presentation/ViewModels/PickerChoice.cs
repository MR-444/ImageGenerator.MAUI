using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// What a Picker binds instead of the value it edits: <c>ItemsSource="{Binding X.Options}"</c>,
/// <c>SelectedItem="{Binding X.Selected}"</c>. It separates a user's pick from everything else
/// that reaches a two-way-bound Picker.
///
/// A WinUI Picker pushes values nobody picked: null or the first item while its ItemsSource is
/// swapped, and null again a dispatcher tick later. Here only a value in <see cref="Options"/>,
/// set outside <see cref="SetOptions"/> and different from the current one, counts as a pick and
/// reaches <c>onPicked</c>. Programmatic changes come in through <see cref="Show"/>, which never
/// calls it, so the owner can persist in <c>onPicked</c> without suppression flags.
///
/// Bind one Picker per instance. After ignoring a push, the choice re-announces its value so the
/// Picker shows it again; two Pickers on one instance could answer each other.
/// </summary>
public sealed class PickerChoice : ObservableObject
{
    private readonly Action<string> _onPicked;
    private readonly ILogger? _logger;
    private readonly string _name;
    private List<string> _options = [];
    private string? _selected;
    private bool _swapping;

    /// <param name="name">Names this Picker in the Debug log, which records every ignored push.</param>
    public PickerChoice(Action<string> onPicked, ILogger? logger = null, string name = "Picker")
    {
        _onPicked = onPicked ?? throw new ArgumentNullException(nameof(onPicked));
        _logger = logger;
        _name = name;
    }

    // A List, not an interface: Picker.ItemsSource is an IList and the bindings are compiled.
    public List<string> Options
    {
        get => _options;
        private set => SetProperty(ref _options, value);
    }

    /// <summary>The value on display. Setting it is the Picker's (and a user's) way in.</summary>
    public string? Selected
    {
        get => _selected;
        set
        {
            if (_swapping || value is null || !_options.Contains(value))
            {
                // Not a pick. If the Picker now shows something else, give it the value back,
                // but only one it can display, so this cannot start a loop.
                var reannounce = !_swapping && value != _selected && _selected is not null && _options.Contains(_selected);
                _logger?.LogDebug(
                    "{Picker}: ignored push \"{Value}\" (swapping={Swapping}, current=\"{Current}\", reannounce={Reannounce})",
                    _name, value, _swapping, _selected, reannounce);
                if (reannounce) OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selected, value)) return;
            _logger?.LogDebug("{Picker}: picked \"{Value}\"", _name, value);
            _onPicked(value);
        }
    }

    /// <summary>
    /// Replaces the list. Whatever the Picker pushes during the swap is ignored. The current value
    /// is announced again afterwards, because the swap reset the Picker's selection; that happens
    /// outside the swap, so a push the Picker makes in reply is treated like any other.
    /// </summary>
    public void SetOptions(IEnumerable<string>? options)
    {
        _swapping = true;
        try
        {
            Options = options is null ? [] : [.. options];
        }
        finally
        {
            _swapping = false;
        }

        OnPropertyChanged(nameof(Selected));
    }

    /// <summary>Shows a value set elsewhere (a fallback, a restore). Never calls <c>onPicked</c>.</summary>
    public void Show(string? value) => SetProperty(ref _selected, value, nameof(Selected));
}
