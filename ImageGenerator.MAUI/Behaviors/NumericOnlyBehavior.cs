namespace ImageGenerator.MAUI.Behaviors;

/// <summary>
/// A behavior that restricts input in an Entry control to numeric-only values.
/// </summary>
/// <remarks>
/// This behavior ensures that only numeric characters are permitted within the associated Entry.
/// Whenever non-numeric input is entered, it is filtered out automatically.
/// </remarks>
public class NumericOnlyBehavior : Behavior<Entry>
{
    /// <summary>
    /// Attaches the behavior to the specified <see cref="Entry"/> control and subscribes to the necessary event.
    /// </summary>
    /// <param name="entry">The <see cref="Entry"/> control to which the behavior is being attached.</param>
    protected override void OnAttachedTo(Entry entry)
    {
        entry.TextChanged += OnEntryTextChanged;
        base.OnAttachedTo(entry);
    }

    /// Detaches the behavior from the specified Entry. This method is triggered when the behavior is removed from the Entry.
    /// <param name="entry">The Entry control from which the behavior is being detached.</param>
    protected override void OnDetachingFrom(Entry entry)
    {
        entry.TextChanged -= OnEntryTextChanged;
        base.OnDetachingFrom(entry);
    }

    /// <summary>
    /// Handles the TextChanged event for an Entry control. Ensures that only numeric input is allowed
    /// by filtering out non-numeric characters from the Entry's text value.
    /// </summary>
    /// <param name="sender">The source of the event, typically the Entry control to which this behavior is attached.</param>
    /// <param name="e">The event arguments containing information about the text changes.</param>
    private static void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry) return;
        var filteredText = string.Concat(e.NewTextValue.Where(char.IsDigit));
            
        if (entry.Text != filteredText)
        {
            entry.Text = filteredText;
        }
    }
} 