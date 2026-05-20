using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

// Coordinator-level tests covering the empty-batch guard, re-entrancy guard, and the
// cancellation-drain-but-finish-in-flight contract. The VM-integration tests in
// GeneratorViewModelTests cover the full file-order / Jobs.Insert ordering path.
public class BatchCoordinatorTests
{
    private static ImageGenerationParameters BuildParams() => new()
    {
        ApiToken = "tok",
        Model = ModelConstants.Flux.Pro11,
        Prompt = "ignored — overridden per prompt",
        AspectRatio = "1:1",
        Width = 1024,
        Height = 1024,
        OutputFormat = ImageOutputFormat.Png,
        OutputQuality = 90,
        SafetyTolerance = 3,
        Seed = 42,
        RandomizeSeed = false,
    };

    private static BatchCoordinator Build(
        Func<GenerationJob, Task>? runJob = null,
        Action<GenerationJob>? enqueueJob = null)
    {
        var parameters = BuildParams();
        return new BatchCoordinator(
            promptBatchParser: new Mock<IPromptBatchParser>().Object,
            parametersAccessor: () => parameters,
            enqueueJob: enqueueJob ?? (_ => { }),
            runJob: runJob ?? (j => Task.CompletedTask),
            setStatus: (_, _) => { },
            addAsInputAsync: _ => Task.CompletedTask);
    }

    [Fact]
    public async Task RunBatchAsync_EmptyPrompts_NoOp()
    {
        var ran = false;
        var coord = Build(runJob: _ => { ran = true; return Task.CompletedTask; });

        await coord.RunBatchAsync(Array.Empty<string>());

        ran.Should().BeFalse();
        coord.IsBatchRunning.Should().BeFalse();
    }

    [Fact]
    public async Task RunBatchAsync_TogglesIsBatchRunning_FalseAtFinish()
    {
        var coord = Build(runJob: j =>
        {
            j.StatusKind = StatusKind.Success;
            return Task.CompletedTask;
        });

        await coord.RunBatchAsync(new[] { "a", "b" });

        coord.IsBatchRunning.Should().BeFalse("finally block clears the flag");
    }

    [Fact]
    public async Task RunBatchAsync_ReEntrancyGuard_SecondCallIsNoOp()
    {
        var firstStarted = new TaskCompletionSource();
        var firstCanFinish = new TaskCompletionSource();
        var calls = 0;
        var coord = Build(runJob: async j =>
        {
            Interlocked.Increment(ref calls);
            firstStarted.TrySetResult();
            await firstCanFinish.Task;
            j.StatusKind = StatusKind.Success;
        });

        var firstBatch = coord.RunBatchAsync(new[] { "one" });
        await firstStarted.Task;

        // Second call while first is in-flight should be rejected by the IsBatchRunning guard.
        await coord.RunBatchAsync(new[] { "two", "three" });
        calls.Should().Be(1, "second batch must not start while the first is running");

        firstCanFinish.SetResult();
        await firstBatch;
    }

    [Fact]
    public async Task RunBatchAsync_EnqueuesJobsInOriginalFileOrder()
    {
        var enqueued = new List<GenerationJob>();
        var coord = Build(
            runJob: j => { j.StatusKind = StatusKind.Success; return Task.CompletedTask; },
            enqueueJob: enqueued.Add);

        await coord.RunBatchAsync(new[] { "first", "second", "third" });

        // Insert is reversed by the coordinator so the host's Insert(0, …) places the FIRST
        // prompt at the top. Verify by checking the prompts on the captured-at-enqueue order
        // (which is the reverse of file order — last in, first inserted at index 0).
        enqueued.Should().HaveCount(3);
        enqueued[0].Parameters.Prompt.Should().Be("third");
        enqueued[1].Parameters.Prompt.Should().Be("second");
        enqueued[2].Parameters.Prompt.Should().Be("first");
    }

    [Fact]
    public async Task CancelBatch_DuringInFlight_DrainsQueueButFinishesCurrent()
    {
        var firstStarted = new TaskCompletionSource();
        var firstCanFinish = new TaskCompletionSource();
        var runs = new List<string>();
        var coord = Build(runJob: async j =>
        {
            runs.Add(j.Parameters.Prompt);
            if (runs.Count == 1)
            {
                firstStarted.TrySetResult();
                await firstCanFinish.Task;
            }
            j.StatusKind = StatusKind.Success;
        });

        var batchTask = coord.RunBatchAsync(new[] { "first", "second", "third" });
        await firstStarted.Task;

        coord.CancelBatchCommand.Execute(null);
        firstCanFinish.SetResult();

        await batchTask;

        runs.Should().ContainSingle("only the in-flight job continues; queued jobs are drained");
        runs[0].Should().Be("first");
    }
}
