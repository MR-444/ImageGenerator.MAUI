using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Presentation.Views;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CrashLogger.Install();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif
        // 1) Add the Refit client
        builder.Services.AddRefitClient<IReplicateApi>("https://api.replicate.com");

        // 2) Per-model descriptors. Each registers as itself + every narrow interface it
        //    implements, forwarded to the same singleton instance. Adding a new model is now
        //    a single-line edit here plus one new descriptor file.
        builder.Services
            .AddModelDescriptor<Flux11ProDescriptor>()
            .AddModelDescriptor<Flux11ProUltraDescriptor>()
            .AddModelDescriptor<Flux2Klein4bDescriptor>()
            .AddModelDescriptor<Flux2Flex2Descriptor>()
            .AddModelDescriptor<Flux2Pro2Descriptor>()
            .AddModelDescriptor<Flux2Max2Descriptor>()
            .AddModelDescriptor<GptImage15Descriptor>()
            .AddModelDescriptor<GptImage2Descriptor>()
            .AddModelDescriptor<NanoBanana2Descriptor>();

        builder.Services.AddSingleton<IModelDescriptorRegistry, ModelDescriptorRegistry>();

        // 3) Register your services and ViewModels
        builder.Services.AddSingleton<IImageEncoderProvider, ImageEncoderProvider>();
        builder.Services.AddSingleton<IImageFileService, ImageFileService>();
        builder.Services.AddSingleton<IReplicateImageGenerationService, ReplicateImageGenerationService>();
        builder.Services.AddSingleton<IImageGenerationService>(sp => sp.GetRequiredService<IReplicateImageGenerationService>());
        builder.Services.AddSingleton<IModelCatalogService, ModelCatalogService>();

        // 3a) VM collaborators carved out of the original god-class GeneratorViewModel (M1).
        builder.Services.AddSingleton<IApiTokenStore, ApiTokenStore>();
        builder.Services.AddSingleton<IJobRunner, JobRunner>();
        builder.Services.AddSingleton<IModelCatalogCoordinator, ModelCatalogCoordinator>();

        builder.Services.AddTransient<GeneratorViewModel>();

        // 4) Register MainPage so it (and its constructor) can be injected
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
