using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// What the user is told when a Replicate model reaches the picker without a field list in this
/// build. This build will not call such a model, so the note is a lockout with a reason — and the
/// reason has to name the model, because "not supported" without one reads as an insult to whoever
/// made it. Part of <see cref="GeneratorViewModelTests"/> to reuse its mocks.
/// </summary>
public partial class GeneratorViewModelTests
{
    [Fact]
    public void ModelFieldsNote_ModelWithADescriptor_SaysNothing()
    {
        _viewModel.Parameters.Model = ModelConstants.OpenAI.GptImage26SunburstOnReplicate;

        // Null, not empty: the Label's visibility runs off NonEmptyStringToBool, so "" would still
        // leave an empty row in the card for every normal model.
        _viewModel.ModelFieldsNote.Should().BeNull();
    }

    [Fact]
    public void ModelFieldsNote_UnknownReplicatePath_NamesTheModelAndSaysItWillNotRun()
    {
        _viewModel.Parameters.Model = "stability-ai/some-new-model";

        // Deliberately not a full sentence match: this is the user's copy, and a test that freezes it
        // turns every wording pass into a test failure. What matters is that the note names which
        // model it means and states that nothing will be sent.
        _viewModel.ModelFieldsNote.Should().NotBeNull()
            .And.Contain("stability-ai/some-new-model")
            .And.Contain("not supported");
    }

    [Theory]
    // Neither is a descriptor gap: Pollinations speaks one uniform API for every id it lists, and a
    // ComfyUI entry is the user's own workflow. Flagging them would train the user to ignore the note.
    [InlineData(ModelConstants.Pollinations.PrefixSlash + "flux")]
    [InlineData(ModelConstants.ComfyUi.PrefixSlash + "sdXL-workflow.json")]
    public void ModelFieldsNote_UniformProviders_SayNothing(string modelId)
    {
        _viewModel.Parameters.Model = modelId;

        _viewModel.ModelFieldsNote.Should().BeNull();
    }

    [Fact]
    public void ModelFieldsNote_RaisesWhenThePickerMovesToAnUnknownModel()
    {
        // The note is computed from Parameters.Model, but the UI only re-reads it on the
        // ProviderFilter.PropertyChanged hook — the one notification a model switch produces.
        var raised = 0;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.ModelFieldsNote)) raised++;
        };

        _viewModel.ProviderFilter.ApplyCatalog(
            [new ModelOption("Known model", ModelConstants.Flux.Klein4b, ProviderConstants.Replicate),
             new ModelOption("Unknown model", "stability-ai/some-new-model", ProviderConstants.Replicate)]);
        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == "stability-ai/some-new-model");

        raised.Should().BeGreaterThan(0);
        _viewModel.ModelFieldsNote.Should().NotBeNull();
    }

    [Fact]
    public void ModelFieldsNote_RaisesWhenParametersModelChangesAndThePickerCannotFollow()
    {
        // The other test covers the picker moving. This covers the only way it cannot: a model id the
        // catalog does not contain never becomes SelectedModel, so the ProviderFilter hook that the
        // note also hangs off stays silent, and a stale note would go on naming the previous model.
        // Remix-from-an-unknown-model and any future parameter-driven entry land here.
        var raised = 0;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.ModelFieldsNote)) raised++;
        };

        _viewModel.Parameters.Model = "stability-ai/some-new-model";

        raised.Should().BeGreaterThan(0,
            "the note names the selected model, so it has to be re-read whenever Parameters.Model changes");
        _viewModel.ModelFieldsNote.Should().NotBeNull();
    }

    [Fact]
    public void SelectModel_WithoutFieldList_LeavesTheSavedAspectRatioAlone()
    {
        // A model this build refuses to call offers no aspect ratio and sends no aspect ratio, so it
        // has no opinion worth writing into persisted state — yet switching models runs the capability
        // refresh, which snaps the current ratio to the new model's first option whenever the list no
        // longer contains it. A single dummy value in FallbackReplicateDescriptor would silently reset
        // 16:9 to 1:1 here, and the user would meet that ratio on their next model too.
        _viewModel.ProviderFilter.ApplyCatalog(
            [new ModelOption("Some New Model", "stability-ai/some-new-model", ProviderConstants.Replicate)]);
        _viewModel.Parameters.AspectRatio = "16:9";

        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == "stability-ai/some-new-model");

        _viewModel.Parameters.AspectRatio.Should().Be("16:9");
    }
}
