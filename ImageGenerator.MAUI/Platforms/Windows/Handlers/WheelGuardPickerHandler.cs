using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;        // ComboBox (the Picker's platform view)
using Microsoft.UI.Xaml.Input;           // PointerRoutedEventArgs

namespace ImageGenerator.MAUI.Platforms.Windows.Handlers;

/// <summary>A ComboBox that ignores the mouse wheel while its dropdown is CLOSED, so scrolling the
/// page over a Picker never silently changes its selection (e.g. flipping the free Local model to a
/// paid tier — the picker can scroll off-screen, so the change goes unseen). When the dropdown is
/// OPEN the wheel still scrolls the item list normally.</summary>
internal sealed class NoWheelComboBox : ComboBox
{
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        // Closed: do nothing and DON'T mark e.Handled, so the wheel bubbles to the page ScrollViewer
        // (the page scrolls) instead of the ComboBox changing its selected item.
        if (IsDropDownOpen)
            base.OnPointerWheelChanged(e);
    }
}

/// <summary>Swaps the default Picker platform view for one that won't change value on a stray wheel
/// tick. Registered globally in <c>MauiProgram</c> so every Picker in the app is protected.</summary>
internal sealed class WheelGuardPickerHandler : PickerHandler
{
    protected override ComboBox CreatePlatformView() => new NoWheelComboBox();
}
