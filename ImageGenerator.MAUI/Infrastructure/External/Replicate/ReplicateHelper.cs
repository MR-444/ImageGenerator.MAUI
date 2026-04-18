using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Models.Replicate;

namespace ImageGenerator.MAUI.Infrastructure.External.Replicate;

public static class ReplicateHelper
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultMaxPollDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Polls the Replicate predictions endpoint until the prediction reaches a terminal state
    /// (succeeded, failed, canceled) or until cancellation / the max-duration guard trips.
    /// </summary>
    public static async Task<ReplicatePredictionResponse?> PollForOutputAsync(
        IReplicateApi replicateApi,
        string bearerToken,
        string predictionId,
        CancellationToken cancellationToken = default,
        TimeSpan? maxDuration = null,
        TimeSpan? pollInterval = null)
    {
        var deadline = DateTimeOffset.UtcNow + (maxDuration ?? DefaultMaxPollDuration);
        var delay = pollInterval ?? DefaultPollInterval;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Replicate prediction {predictionId} did not reach a terminal state within {(maxDuration ?? DefaultMaxPollDuration).TotalMinutes:F0} minutes.");
            }

            var prediction = await replicateApi.GetPredictionAsync(bearerToken, predictionId, cancellationToken);

            if (prediction == null)
            {
                return null;
            }

            switch (prediction.Status)
            {
                case "succeeded":
                case "failed":
                case "canceled":
                    return prediction;

                case "starting":
                case "processing":
                case null:
                case "":
                    await Task.Delay(delay, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unexpected Replicate prediction status '{prediction.Status}' for prediction {predictionId}.");
            }
        }
    }
}
