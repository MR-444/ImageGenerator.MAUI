using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Tests.ViewModels;

// Coordinator-level tests covering the catalog application path, saved-state restore, and
// cross-provider sync — the parts the VM-integration tests don't reach in isolation.
public class ProviderFilterCoordinatorTests
{
    private static readonly ModelOption[] Seeds =
    {
        new("Flux 1.1 Pro", "black-forest-labs/flux-1.1-pro", "Black Forest Labs"),
        new("Pollinations Flux", "pollinations/flux", "Pollinations"),
    };

    private static ProviderFilterCoordinator Build(
        IReadOnlyList<ModelOption>? seeds = null,
        Func<string>? currentModel = null,
        Action<string>? setParametersModel = null,
        Action<string?>? refreshCapabilities = null)
    {
        return new ProviderFilterCoordinator(
            initialSeeds: seeds ?? Seeds,
            currentModelAccessor: currentModel ?? (() => string.Empty),
            setParametersModel: setParametersModel ?? (_ => { }),
            refreshCapabilities: refreshCapabilities ?? (_ => { }));
    }

    [Fact]
    public void Ctor_PopulatesAllModelsAndProviders_FromSeeds()
    {
        var coord = Build();

        coord.AllModels.Should().HaveCount(2);
        coord.Providers.Should().ContainInOrder(ProviderFilterCoordinator.AllProvidersLabel, "Black Forest Labs", "Pollinations");
        coord.FilteredModels.Should().HaveCount(2);
    }

    [Fact]
    public void ApplyCatalog_ReplacesAllModelsAndProviders()
    {
        var coord = Build();
        var merged = new[]
        {
            new ModelOption("Nano Banana 2", "google/nano-banana-2", "Google"),
            new ModelOption("Flux 1.1 Pro", "black-forest-labs/flux-1.1-pro", "Black Forest Labs"),
        };

        coord.ApplyCatalog(merged);

        coord.AllModels.Should().HaveCount(2);
        coord.Providers.Should().ContainInOrder(ProviderFilterCoordinator.AllProvidersLabel, "Black Forest Labs", "Google");
    }

    [Fact]
    public void ApplyCatalog_SuppressionFlagFalseAfterReturn()
    {
        var coord = Build();

        coord.ApplyCatalog(Seeds);

        coord.SuppressModelPersist.Should().BeFalse("suppression must clear in the finally block");
    }

    [Fact]
    public void RestoreSelectedModel_WhenValueInCatalog_SetsSelectedModel()
    {
        var coord = Build();

        coord.RestoreSelectedModel("pollinations/flux");

        coord.SelectedModel.Should().NotBeNull();
        coord.SelectedModel!.Value.Should().Be("pollinations/flux");
    }

    [Fact]
    public void RestoreSelectedModel_WhenValueNotInCatalog_NoOp()
    {
        var coord = Build();
        var before = coord.SelectedModel;

        coord.RestoreSelectedModel("never/existed");

        coord.SelectedModel.Should().Be(before);
    }

    [Fact]
    public void RestoreSelectedModel_DoesNotInvokeSetParametersModel()
    {
        // The restore path writes SelectedModel, which triggers OnSelectedModelChanged. The
        // change handler only invokes setParametersModel when the current model differs — keep
        // the suppression contract intact via the currentModelAccessor matching the new value.
        var changed = false;
        var coord = Build(
            currentModel: () => "pollinations/flux",
            setParametersModel: _ => changed = true);

        coord.RestoreSelectedModel("pollinations/flux");

        changed.Should().BeFalse("the current model already matches; no Parameters write is needed");
    }

    [Fact]
    public void SelectedProvider_Changed_FiltersModels()
    {
        var coord = Build();

        coord.SelectedProvider = "Pollinations";

        coord.FilteredModels.Should().ContainSingle();
        coord.FilteredModels[0].Value.Should().Be("pollinations/flux");
    }

    [Fact]
    public void SyncSelectionFromParameters_AdjustsProviderAndModel()
    {
        var coord = Build();
        coord.SelectedProvider = "Black Forest Labs";  // narrows FilteredModels to one entry

        coord.SyncSelectionFromParameters("pollinations/flux");

        coord.SelectedProvider.Should().Be("Pollinations", "the provider auto-switches to match the new model");
        coord.SelectedModel!.Value.Should().Be("pollinations/flux");
    }
}
