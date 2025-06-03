namespace ImageGenerator.MAUI.Models.Replicate;

/// <summary>
/// Provides utility methods for interacting with the Replicate API in image generation workflows.
/// </summary>
public static class ReplicateHelper
{
    /// <summary>
    /// Polls the Replicate API for the final output of a prediction by periodically checking its status.
    /// </summary>
    /// <param name="replicateApi">The interface to interact with the Replicate API.</param>
    /// <param name="bearerToken">The bearer token used for authentication with the Replicate API.</param>
    /// <param name="predictionId">The unique identifier of the prediction to monitor.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result is the <see cref="ReplicatePredictionResponse"/> object
    /// containing the final prediction output if successful, the error details if failed, or null if no response was obtained.
    /// </returns>
    public static async Task<ReplicatePredictionResponse?> PollForOutputAsync(
        IReplicateApi replicateApi,
        string bearerToken,
        string predictionId
    )
    {
        while (true)
        {
            // Fetch the latest status from Replicate
            var prediction = await replicateApi.GetPredictionAsync(
                bearerToken, // e.g. "Bearer YOUR_API_TOKEN"
                predictionId
            );

            if (prediction == null)
            {
                return null;
            }

            switch (prediction.Status)
            {
                case "succeeded":
                    // Return the final output if succeeded
                    return prediction;
                case "failed":
                    // Return the prediction with error details instead of null
                    return prediction;
                default:
                    // Wait briefly before the next check
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    break;
            }
        }
    }
}