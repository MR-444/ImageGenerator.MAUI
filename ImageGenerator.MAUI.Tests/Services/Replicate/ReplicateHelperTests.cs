using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Models.Replicate;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services.Replicate;

public class ReplicateHelperTests
{
    private readonly Mock<IReplicateApi> _api = new();
    private const string Token = "Bearer test";
    private const string PredictionId = "pred-1";

    // Tight intervals so the suite stays fast — every test that polls passes a short
    // pollInterval/maxDuration explicitly. Defaults (3s/5min) would balloon test runtime.
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task PollForOutput_TerminalSucceeded_ReturnsPrediction()
    {
        var pred = new ReplicatePredictionResponse { Status = "succeeded", Output = new[] { "url" } };
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pred);

        var result = await ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        result.Should().BeSameAs(pred);
    }

    [Fact]
    public async Task PollForOutput_TerminalFailed_ReturnsPredictionDoesNotThrow()
    {
        var pred = new ReplicatePredictionResponse { Status = "failed", Error = "model error" };
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pred);

        var result = await ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        result.Should().NotBeNull();
        result!.Status.Should().Be("failed");
        result.Error.Should().Be("model error");
    }

    [Fact]
    public async Task PollForOutput_TerminalCanceled_ReturnsPredictionDoesNotThrow()
    {
        var pred = new ReplicatePredictionResponse { Status = "canceled" };
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pred);

        var result = await ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        result.Should().NotBeNull();
        result!.Status.Should().Be("canceled");
    }

    [Fact]
    public async Task PollForOutput_PollsMultipleTimesBeforeTerminal()
    {
        var starting = new ReplicatePredictionResponse { Status = "starting" };
        var processing = new ReplicatePredictionResponse { Status = "processing" };
        var succeeded = new ReplicatePredictionResponse { Status = "succeeded" };

        _api.SetupSequence(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(starting)
            .ReturnsAsync(processing)
            .ReturnsAsync(succeeded);

        var result = await ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        result.Should().BeSameAs(succeeded);
        _api.Verify(
            x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task PollForOutput_NullOrEmptyStatus_KeepsPolling()
    {
        var nullStatus = new ReplicatePredictionResponse { Status = null };
        var emptyStatus = new ReplicatePredictionResponse { Status = "" };
        var done = new ReplicatePredictionResponse { Status = "succeeded" };

        _api.SetupSequence(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(nullStatus)
            .ReturnsAsync(emptyStatus)
            .ReturnsAsync(done);

        var result = await ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        result.Should().BeSameAs(done);
    }

    [Fact]
    public async Task PollForOutput_PredictionIsNull_ReturnsNull()
    {
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReplicatePredictionResponse?)null);

        var result = await ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PollForOutput_UnexpectedStatus_ThrowsInvalidOperation()
    {
        var weird = new ReplicatePredictionResponse { Status = "rolled-back" };
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(weird);

        var act = () => ReplicateHelper.PollForOutputAsync(_api.Object, Token, PredictionId, pollInterval: FastPoll);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rolled-back*");
    }

    [Fact]
    public async Task PollForOutput_DeadlineElapsed_ThrowsTimeout()
    {
        var processing = new ReplicatePredictionResponse { Status = "processing" };
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processing);

        var act = () => ReplicateHelper.PollForOutputAsync(
            _api.Object, Token, PredictionId,
            maxDuration: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(20));

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task PollForOutput_PrecanceledToken_ThrowsOperationCanceled()
    {
        var processing = new ReplicatePredictionResponse { Status = "processing" };
        _api.Setup(x => x.GetPredictionAsync(Token, PredictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(processing);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ReplicateHelper.PollForOutputAsync(
            _api.Object, Token, PredictionId,
            cancellationToken: cts.Token,
            pollInterval: FastPoll);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
