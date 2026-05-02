using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ImageGenerator.MAUI.Extensions;

public static class DescriptorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a descriptor type as a singleton, then forwards it under each of the four
    /// narrow interfaces it implements. Each forward resolves to the same instance so the
    /// registry sees one descriptor per model id, not duplicates.
    /// </summary>
    public static IServiceCollection AddModelDescriptor<T>(this IServiceCollection services)
        where T : class
    {
        services.AddSingleton<T>();
        var t = typeof(T);

        if (typeof(IPayloadBuilder).IsAssignableFrom(t))
            services.AddSingleton<IPayloadBuilder>(sp => (IPayloadBuilder)sp.GetRequiredService(t));
        if (typeof(ICapabilityProvider).IsAssignableFrom(t))
            services.AddSingleton<ICapabilityProvider>(sp => (ICapabilityProvider)sp.GetRequiredService(t));
        if (typeof(IMetadataDescriber).IsAssignableFrom(t))
            services.AddSingleton<IMetadataDescriber>(sp => (IMetadataDescriber)sp.GetRequiredService(t));
        if (typeof(ICatalogSeedEntry).IsAssignableFrom(t))
            services.AddSingleton<ICatalogSeedEntry>(sp => (ICatalogSeedEntry)sp.GetRequiredService(t));

        return services;
    }
}
