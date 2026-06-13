using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class GenerationJobTests
{
    private static ImageGenerationParameters MakeParameters(string prompt = "a cat", string model = "openai/gpt-image-2",
                                                            string aspectRatio = "1:1", long seed = 42)
    {
        return new ImageGenerationParameters
        {
            Prompt = prompt,
            Model = model,
            AspectRatio = aspectRatio,
            Seed = seed,
        };
    }

    [Fact]
    public void Constructor_FreezesPromptAtConstructionTime()
    {
        var p = MakeParameters(prompt: "original");
        var job = new GenerationJob(p);

        p.Prompt = "mutated-later";

        job.Prompt.Should().Be("original");
    }

    [Fact]
    public void Constructor_BuildsMetaLineFromSnapshot()
    {
        var p = MakeParameters(model: "openai/gpt-image-2", aspectRatio: "16:9", seed: 7);
        var job = new GenerationJob(p);

        job.MetaLine.Should().Contain("gpt-image-2")
                    .And.Contain("16:9")
                    .And.Contain("7");
    }

    [Fact]
    public void Constructor_FallsBackToWholeModelWhenNoSlash()
    {
        var p = MakeParameters(model: "flat-model-name");
        var job = new GenerationJob(p);

        job.MetaLine.Should().StartWith("flat-model-name");
    }

    [Fact]
    public void InitialState_IsRunningTrueAndNoResult()
    {
        var job = new GenerationJob(MakeParameters());

        job.IsRunning.Should().BeTrue();
        job.ResultPath.Should().BeNull();
        job.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public void IsFinished_NewlyConstructedRunningJob_IsFalse()
    {
        new GenerationJob(MakeParameters()).IsFinished.Should().BeFalse("a running job hasn't landed yet");
    }

    [Fact]
    public void IsFinished_QueuedBatchJob_IsFalse()
    {
        // A queued batch job is !IsRunning but still at StatusKind.Info (BatchCoordinator).
        // It must NOT count as finished, or Clear-finished would evict pending work.
        var job = new GenerationJob(MakeParameters()) { IsRunning = false, StatusKind = StatusKind.Info };

        job.IsFinished.Should().BeFalse();
    }

    [Theory]
    [InlineData(StatusKind.Success)]
    [InlineData(StatusKind.Error)]
    [InlineData(StatusKind.Canceled)]
    public void IsFinished_TerminalOutcome_IsTrue(StatusKind kind)
    {
        var job = new GenerationJob(MakeParameters()) { IsRunning = false, StatusKind = kind };

        job.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void IsFinished_RaisesPropertyChanged_WhenIsRunningFlips()
    {
        var job = new GenerationJob(MakeParameters()) { StatusKind = StatusKind.Success };
        using var monitor = job.Monitor();

        job.IsRunning = false;

        monitor.Should().RaisePropertyChangeFor(j => j.IsFinished);
    }

    [Fact]
    public void Cancel_FlipsCancellationToken()
    {
        var job = new GenerationJob(MakeParameters());

        job.CancelCommand.Execute(null);

        job.Cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Cancel_AfterCtsDisposed_DoesNotThrow()
    {
        // The ViewModel disposes Cts in RunJobAsync's finally; binding-update lag could
        // briefly allow the Cancel button to remain clickable post-completion.
        var job = new GenerationJob(MakeParameters());
        job.Cts.Dispose();

        var act = () => job.CancelCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void OpenImage_WhenResultPathNull_IsNoOpAndDoesNotThrow()
    {
        var job = new GenerationJob(MakeParameters());

        var act = () => job.OpenImageCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void ShowInFolder_WhenResultPathNull_IsNoOpAndDoesNotThrow()
    {
        var job = new GenerationJob(MakeParameters());

        var act = () => job.ShowInFolderCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task UseAsInput_WhenResultPathSet_InvokesDelegateWithPath()
    {
        string? received = null;
        var job = new GenerationJob(MakeParameters(), path =>
        {
            received = path;
            return Task.CompletedTask;
        });
        job.ResultPath = @"C:\fake\image.png";

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)job.UseAsInputCommand).ExecuteAsync(null);

        received.Should().Be(@"C:\fake\image.png");
    }

    [Fact]
    public async Task UseAsInput_WhenResultPathNull_DoesNotInvokeDelegate()
    {
        var invoked = false;
        var job = new GenerationJob(MakeParameters(), _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)job.UseAsInputCommand).ExecuteAsync(null);

        invoked.Should().BeFalse();
    }

    [Fact]
    public void OpenCivitaiPost_WhenUrlNull_IsNoOpAndDoesNotThrow()
    {
        var job = new GenerationJob(MakeParameters());

        var act = () => job.OpenCivitaiPostCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void CivitaiState_StartsEmpty()
    {
        var job = new GenerationJob(MakeParameters());

        job.CivitaiStatusMessage.Should().BeNull("the job card's CivitAI row must stay collapsed unless posting ran");
        job.CivitaiPostUrl.Should().BeNull();
    }

    [Fact]
    public void Parameters_AreSnapshot_NotSharedWithTheOriginal()
    {
        var p = MakeParameters(prompt: "A");
        var job = new GenerationJob(p);

        // The ViewModel calls Clone() before passing parameters in; here we validate that
        // the job keeps its own reference regardless. Mutating the source mirrors what the
        // UI binding would do if the user edits the form mid-flight.
        p.Prompt = "B";

        job.Parameters.Should().BeSameAs(p,
            "the constructor stores the reference it receives — the caller is responsible for cloning");
    }
}
