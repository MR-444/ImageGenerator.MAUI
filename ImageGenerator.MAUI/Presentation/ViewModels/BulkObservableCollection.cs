using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can swap its whole contents in one notification.
/// </summary>
/// <remarks>
/// Model lists are rebuilt wholesale on every refresh, and OpenRouter's runs to hundreds of entries.
/// Clear-then-Add raises one change per entry, and every row watching the list rebuilt its own copy on
/// each of them - a 300-model refresh reached one row as 45,451 picker updates and froze the UI thread.
/// <see cref="ReplaceAll"/> raises a single <see cref="NotifyCollectionChangedAction.Reset"/>.
/// </remarks>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
