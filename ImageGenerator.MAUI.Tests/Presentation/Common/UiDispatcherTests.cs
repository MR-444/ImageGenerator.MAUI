using FluentAssertions;
using ImageGenerator.MAUI.Presentation.Common;

namespace ImageGenerator.MAUI.Tests.Presentation.Common;

/// <summary>
/// The one contract this helper has ever broken: an action runs exactly once per call, and if it
/// fails, it fails with its own exception.
///
/// The shape until 2026-11-19 wrapped the action in a bare <c>try/catch</c> whose fallback re-ran it,
/// because a missing dispatcher and a failing action look identical to a catch that reads no
/// exception. On a device the probe answers true, so the action ran inside the try and its own
/// exception triggered the retry: <c>Jobs.Insert(0, job)</c> queued the same job twice — two cards,
/// one object, so cancelling one moved the other — and <c>job.PropertyChanged +=</c> subscribed a
/// second copy no later <c>-=</c> would remove.
/// </summary>
public class UiDispatcherTests
{
    // MainThreadProbe is process-global and xunit runs classes in parallel, so each test hands the
    // seam back. Leaking it could not change another test's outcome anyway: with or without a probe,
    // a host with no dispatcher executes the action inline exactly once — which is what the three
    // tests below each pin from a different side.
    [Fact]
    public void DispatchToUi_WhenAlreadyOnTheMainThread_RunsTheActionOnceAndRethrowsItsOwnException()
    {
        // The branch production takes and no test host can enter on its own: IsMainThread true, which
        // is where the action used to sit inside the try.
        UiDispatcher.MainThreadProbe = () => true;
        try
        {
            var runs = 0;
            var expected = new InvalidOperationException("the action's own failure");

            var act = () => UiDispatcher.DispatchToUi(() =>
            {
                runs++;
                throw expected;
            });

            // Not wrapped, not replaced: the caller has to see the throw it actually made, with its
            // own stack. Under the old catch the caller saw a second, later throw instead.
            act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
            runs.Should().Be(1, "a UI throw is not a missing dispatcher; re-running Jobs.Insert or a " +
                                   "PropertyChanged += a second time is the bug this pins");
        }
        finally { UiDispatcher.MainThreadProbe = null; }
    }

    [Fact]
    public void DispatchToUi_WithNoDispatcherAtAll_StillRunsTheAction()
    {
        // No probe and a host where MainThread throws: the fallback is the only thing that gets the
        // work done, so the action must not be swallowed along with the dispatcher.
        UiDispatcher.MainThreadProbe = null;

        var runs = 0;
        UiDispatcher.DispatchToUi(() => runs++);

        runs.Should().Be(1);
    }

    [Fact]
    public void DispatchToUi_WhenSchedulingFails_RunsTheActionOnceInsteadOfDroppingIt()
    {
        // Off the UI thread with no usable dispatcher: scheduling fails before the action started, so
        // running it here is still one execution. This is the difference between a fallback that
        // protects a missing dispatcher and one that protects a running action.
        UiDispatcher.MainThreadProbe = () => false;
        try
        {
            var runs = 0;
            var act = () => UiDispatcher.DispatchToUi(() => runs++);

            act.Should().NotThrow();
            runs.Should().Be(1);
        }
        finally { UiDispatcher.MainThreadProbe = null; }
    }
}
