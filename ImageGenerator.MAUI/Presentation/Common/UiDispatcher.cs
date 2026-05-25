using Microsoft.Maui.ApplicationModel;

namespace ImageGenerator.MAUI.Presentation.Common;

/// <summary>
/// Marshals an action onto the UI thread. Extracted from the three sub-VM coordinators that
/// each carried an identical private copy (GeneratorViewModel, BatchCoordinator, GalleryViewModel).
/// </summary>
public static class UiDispatcher
{
    public static void DispatchToUi(Action action)
    {
        try
        {
            if (MainThread.IsMainThread) action();
            else MainThread.BeginInvokeOnMainThread(action);
        }
        catch
        {
            // MainThread throws in unit-test contexts where WinRT isn't initialised.
            // Running synchronously is safe because tests run on a single thread anyway.
            action();
        }
    }
}
