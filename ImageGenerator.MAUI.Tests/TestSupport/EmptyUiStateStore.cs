using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Moq;

namespace ImageGenerator.MAUI.Tests.TestSupport;

/// <summary>
/// A state store with nothing in it — the state of a first launch, not the state of "the test did not
/// set anything up".
///
/// <para>
/// The difference is the reason this exists. <c>ModelChoiceState</c> opens on whatever the store hands
/// back, and a bare <see cref="Mock{T}"/> hands back <c>null</c> for <see cref="IUiStateStore.LoadLlmProvider"/>
/// where a real store hands back the caller's fallback. A null answer makes the row fall through to the
/// first provider it was offered, so every "this page opens on …" test written against a bare mock grades
/// the row's last-ditch fallback and passes whatever the page asked for. Local sits first in that
/// fallback order for enum-order reasons, which is how such a test can look like a pass on the very
/// property it claims to check.
/// </para>
/// </summary>
internal static class EmptyUiStateStore
{
    internal static Mock<IUiStateStore> New()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmProvider(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>()))
            .Returns((ModelRole _, ModelProvider fallback) => fallback);
        return store;
    }
}
