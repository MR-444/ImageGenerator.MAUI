using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Application.Services;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;
using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.External.Pollinations;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Presentation.Views;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace ImageGenerator.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Install before anything else so a DI/builder failure during the rest of this
        // method has a place to land. The wrapping try/catch below relies on this.
        CrashLogger.Install();

        try
        {
            return BuildApp();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("MauiProgram.CreateMauiApp", ex);
            throw;
        }
    }

    private static MauiApp BuildApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // NLog config (file target + Infrastructure.External.* Debug rule) was set up by
        // CrashLogger.Install() above; here we just route MEL through NLog so every
        // ILogger<T> resolved from DI lands in the same physical app.log.
        builder.Logging.ClearProviders().AddNLog();
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
            .AddModelDescriptor<NanoBanana2Descriptor>()
            .AddModelDescriptor<PollinationsFluxDescriptor>()
            .AddModelDescriptor<PollinationsZimageDescriptor>()
            .AddModelDescriptor<PollinationsQwenImageDescriptor>();

        builder.Services.AddSingleton<IModelDescriptorRegistry, ModelDescriptorRegistry>();

        // 3) Register your services and ViewModels
        builder.Services.AddSingleton<IImageEncoderProvider, ImageEncoderProvider>();
        builder.Services.AddSingleton<IImageFileService, ImageFileService>();
        // Per-provider concrete services are registered as themselves so the dispatcher can
        // take both via ctor and route per-call. The dispatcher is what IImageGenerationService
        // resolves to.
        builder.Services.AddSingleton<ReplicateImageGenerationService>();
        builder.Services.AddSingleton<PollinationsImageGenerationService>();
        builder.Services.AddSingleton<IImageGenerationService, ImageGenerationDispatcher>();
        builder.Services.AddSingleton<IModelCatalogService, ModelCatalogService>();
        builder.Services.AddSingleton<IPollinationsCatalogService, PollinationsCatalogService>();
        builder.Services.AddSingleton<IGalleryService>(_ => new GalleryService());
        builder.Services.AddSingleton<IFileLauncher, FileLauncher>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();

        // 3a) VM collaborators carved out of the original god-class GeneratorViewModel (M1).
        builder.Services.AddSingleton<IApiTokenStore, ApiTokenStore>();
        builder.Services.AddSingleton<IPollinationsTokenStore, PollinationsTokenStore>();
        builder.Services.AddSingleton<IUiStateStore, UiStateStore>();
        builder.Services.AddSingleton<IJobRunner, JobRunner>();
        builder.Services.AddSingleton<IModelCatalogCoordinator, ModelCatalogCoordinator>();
        builder.Services.AddSingleton<IPromptBatchParser, PromptBatchParser>();

        // GeneratorViewModel is registered as Singleton because it owns session state
        // (Jobs collection, SelectedImages, IsBatchRunning) that must survive
        // Shell.GoToAsync("//MainPage?addInput=…") navigations from the gallery detail
        // page. Without Singleton, "Use as input" rebuilds the VM and drops the
        // in-flight batch + history. MainPage matches the VM lifetime; it's the root
        // ShellContent (not a pushable route), so Shell never instantiates it twice.
        builder.Services.AddSingleton<GeneratorViewModel>();
        builder.Services.AddTransient<GalleryViewModel>();
        builder.Services.AddTransient<GalleryItemDetailViewModel>();

        // 4) Register MainPage so it (and its constructor) can be injected. See the
        //    Singleton rationale above the GeneratorViewModel registration.
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<GalleryPage>();
        builder.Services.AddTransient<GalleryItemDetailPage>();

        return builder.Build();
    }
}
