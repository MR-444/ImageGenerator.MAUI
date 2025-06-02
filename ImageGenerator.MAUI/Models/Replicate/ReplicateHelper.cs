namespace ImageGenerator.MAUI.Models.Replicate;

public static class ReplicateHelper
{
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