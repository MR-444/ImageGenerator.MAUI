using Refit;

namespace ImageGenerator.MAUI.Extensions;

/// <summary>
/// Provides extension methods for integrating Refit clients into the dependency injection container.
/// </summary>
public static class RefitServiceExtensions
{
    /// <summary>
    /// Adds a typed Refit client with the specified base address to the service collection.
    /// </summary>
    /// <typeparam name="T">The interface type representing the Refit client.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the Refit client is added.</param>
    /// <param name="baseAddress">The base address to use for HTTP requests made by the Refit client.</param>
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
