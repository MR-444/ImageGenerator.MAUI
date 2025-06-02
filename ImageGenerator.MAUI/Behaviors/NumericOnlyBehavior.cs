namespace ImageGenerator.MAUI.Behaviors;

public class NumericOnlyBehavior : Behavior<Entry>
{
    protected override void OnAttachedTo(Entry entry)
    {
        entry.TextChanged += OnEntryTextChanged;
        base.OnAttachedTo(entry);
    }

    protected override void OnDetachingFrom(Entry entry)
    {
        entry.TextChanged -= OnEntryTextChanged;
        base.OnDetachingFrom(entry);
    }

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