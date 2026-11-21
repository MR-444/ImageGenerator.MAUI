using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// One provider's model list, as a role sees it: something to pick from, something to press when the
/// list looks stale, and somewhere the provider's answer appears.
///
/// <para>
/// Deliberately an interface over whatever already holds those three, rather than a type that owns them.
/// The lists are built by different owners on purpose — Ollama's comes from a local server together with
/// the reconciliation of every role's pick, Anthropic's arrives paged from <c>GET /v1/models</c> — and a
/// state object that binds to <em>these three things</em> does not have to know that, while each owner
/// keeps the refresh logic it already earned.
/// </para>
/// </summary>
public interface IModelSource
{
    /// <summary>Live view of what can be picked. The owner mutates it; implementations re-read it.</summary>
    IReadOnlyList<string> Models { get; }

    /// <summary>What the provider last said, or null for "nothing to report".</summary>
    string? Status { get; }

    /// <summary>Ask the provider again. Never throws for a provider-side failure: the answer arrives on
    /// <see cref="Status"/>, which is the whole point of the per-card status lines.</summary>
    Task RefreshAsync();

    /// <summary>Raised when <see cref="Models"/> changes content.</summary>
    event EventHandler? ModelsChanged;

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    event EventHandler? StatusChanged;
}

/// <summary>
/// Adapts a list + status + refresh command that already live on another object — in practice
/// <see cref="AppSettingsCoordinator"/> — to <see cref="IModelSource"/>.
/// </summary>
/// <param name="statusProperty">Name of the status property to watch on <paramref name="statusOwner"/>.
/// A string rather than a typed accessor because the statuses are source-generated observable
/// properties; <c>ModelSourceTests</c> pins that the name still resolves, which is the only way a rename
/// is caught.</param>
/// <param name="refresh">Asks the owner to refresh, and completes when it has. Takes a task-returning
/// delegate rather than an <see cref="ICommand"/> on purpose: firing a command and returning
/// <see cref="Task.CompletedTask"/> would let every caller await a refresh that had not happened yet.</param>
public sealed class ModelSource : IModelSource
{
    private readonly ObservableCollection<string> _models;
    private readonly Func<string?> _readStatus;
    private readonly Func<Task> _refresh;

    public ModelSource(
        ObservableCollection<string> models,
        INotifyPropertyChanged statusOwner,
        string statusProperty,
        Func<string?> readStatus,
        Func<Task> refresh)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _readStatus = readStatus ?? throw new ArgumentNullException(nameof(readStatus));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));

        _models.CollectionChanged += (_, _) => ModelsChanged?.Invoke(this, EventArgs.Empty);
        statusOwner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == statusProperty)
                StatusChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public IReadOnlyList<string> Models => _models;

    public string? Status => _readStatus();

    public event EventHandler? ModelsChanged;

    public event EventHandler? StatusChanged;

    public Task RefreshAsync() => _refresh();
}
