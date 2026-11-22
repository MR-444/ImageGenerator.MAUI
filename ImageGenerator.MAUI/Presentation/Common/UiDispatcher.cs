namespace ImageGenerator.MAUI.Presentation.Common;

/// <summary>
/// Marshals an action onto the UI thread. Extracted from the three sub-VM coordinators that
/// each carried an identical private copy (GeneratorViewModel, BatchCoordinator, GalleryViewModel).
/// </summary>
public static class UiDispatcher
{
    /// <summary>
    /// Replaces the <see cref="MainThread"/> probe. Null, always, outside tests: the branch this
    /// seam exists to reach is the one a unit-test host can never enter, because
    /// <see cref="MainThread.IsMainThread"/> throws there instead of answering false (see the
    /// fallback below).
    /// </summary>
    internal static Func<bool>? MainThreadProbe { get; set; }

    public static void DispatchToUi(Action action)
    {
        // Only the probe may fail into the fallback, and only where the action has not started.
        // Wrapping the action itself in a bare catch — the shape until 2026-11-19 — made every UI
        // throw look like "no dispatcher here": on a device IsMainThread is true, so the action ran
        // inside the try, and its exception sent it round a second time with the first stack gone.
        // That is Jobs.Insert(0, job) queueing one prediction twice, and job.PropertyChanged +=
        // subscribing a handler twice with nothing left to remove the extra.
        var onMainThread = Probe();

        if (onMainThread is null or true)
        {
            // Either this is the UI thread, or there is no dispatcher to move to and the caller is a
            // single-threaded unit test. Running inline is the only thing that gets the work done.
            action();
            return;
        }

        try
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
        catch
        {
            // Scheduling failed before the action ever started, so running it here is still exactly
            // one execution.
            action();
        }
    }

    /// <summary>The dispatcher probe on its own, so nothing but the probe can be mistaken for it.</summary>
    private static bool? Probe()
    {
        try
        {
            return MainThreadProbe is { } probe ? probe() : MainThread.IsMainThread;
        }
        catch
        {
            // Unit-test contexts have no WinRT dispatcher to ask.
            return null;
        }
    }
}
