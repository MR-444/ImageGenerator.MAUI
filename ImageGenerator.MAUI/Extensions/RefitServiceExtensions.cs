using Refit;

namespace ImageGenerator.MAUI.Extensions;

public static class RefitServiceExtensions
{
    public static void AddRefitClient<T>(this IServiceCollection services, string baseAddress) where T : class
    {
        services
            .AddRefitClient<T>()
            .ConfigureHttpClient(client =>
            {
                // This sets the base address for the client
                client.BaseAddress = new Uri(baseAddress);
            });
    }
}
