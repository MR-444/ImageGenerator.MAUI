using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Application.Services;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;
using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.External.Civitai;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
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

        // NLog config (file target + the namespace Debug rules) was set up by
        // CrashLogger.Install() above; here we just route MEL through NLog so every
        // ILogger<T> resolved from DI lands in the same physical app.log.
        // SetMinimumLevel(Trace) is required for the Debug rules to ever fire: MEL's own
        // default filter (Information) would drop LogDebug calls before NLog sees them —
        // the NLog rules, not MEL, are the intended filtering layer.
        builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddNLog();
        // 1) Add the Refit client
        builder.Services.AddRefitClient<IReplicateApi>("https://api.replicate.com");

        // 1a) Named HttpClients for the non-Refit outbound paths. Both reuse the project's
        //     standard resilience pipeline (retry on 5xx/408/429, Polly-owned timeouts) so the
        //     CDN download and Pollinations calls are no longer asymmetric with Refit.
        //     "pollinations" is shared by gen + catalog (same host, same retry budget).
        builder.Services.AddHttpClient(ReplicateImageGenerationService.HttpClientName)
            .ConfigureStandardResilience(
                perAttemptTimeout: TimeSpan.FromSeconds(60),
                totalTimeout: TimeSpan.FromMinutes(3));

        builder.Services.AddHttpClient(PollinationsImageGenerationService.HttpClientName, client =>
                client.BaseAddress = new Uri("https://gen.pollinations.ai"))
            .ConfigureStandardResilience(
                perAttemptTimeout: TimeSpan.FromSeconds(60),
                totalTimeout: TimeSpan.FromMinutes(3));

        // No BaseAddress: the ComfyUI server URL is a runtime setting (UiStateStore), so the
        // service composes absolute URIs per request. Timeouts bound each HTTP call only —
        // the service's own poll loop owns the overall generation deadline.
        builder.Services.AddHttpClient(ComfyUiImageGenerationService.HttpClientName)
            .ConfigureStandardResilience(
                perAttemptTimeout: TimeSpan.FromSeconds(60),
                totalTimeout: TimeSpan.FromMinutes(3));

        // No BaseAddress: the service talks to two hosts (mcp.civitai.com for upload/whoami,
        // civitai.com for the tRPC post creation). 120 s per attempt because upload_image
        // carries the whole image as base64 (~4-11 MB for a 4 MP PNG). Note the standard
        // retry (5xx/408/429) can in rare cases duplicate a post if create succeeded but the
        // response was lost — accepted: posts publish immediately (user decision), so a
        // duplicate is visible right away and deleted on the site in one click.
        builder.Services.AddHttpClient(CivitaiPostingService.HttpClientName)
            .ConfigureStandardResilience(
                perAttemptTimeout: TimeSpan.FromSeconds(120),
                totalTimeout: TimeSpan.FromMinutes(5));

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
            .AddModelDescriptor<IdeogramV4BalancedDescriptor>()
            .AddModelDescriptor<IdeogramV4TurboDescriptor>()
            .AddModelDescriptor<IdeogramV4QualityDescriptor>()
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
        builder.Services.AddSingleton<ComfyUiImageGenerationService>();
        builder.Services.AddSingleton<IImageGenerationService, ImageGenerationDispatcher>();
        builder.Services.AddSingleton<IModelCatalogService, ModelCatalogService>();
        builder.Services.AddSingleton<IPollinationsCatalogService, PollinationsCatalogService>();
        builder.Services.AddSingleton<IComfyUiWorkflowCatalogService, ComfyUiWorkflowCatalogService>();
        builder.Services.AddSingleton<IComfyUiCheckpointService, ComfyUiCheckpointService>();
        builder.Services.AddSingleton<IGalleryService>(_ => new GalleryService());
        builder.Services.AddSingleton<IFileLauncher, FileLauncher>();
        builder.Services.AddSingleton<IFolderPicker, FolderPickerService>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IJsonPromptFileService, JsonPromptFileService>();
        builder.Services.AddSingleton<IMutationLibraryService, MutationLibraryService>();
        // Stateless mutation engine over the full built-in operator set; one instance is fine.
        builder.Services.AddSingleton<Core.Domain.Ideogram.Mutation.CaptionMutationEngine>();

        // 3a) VM collaborators carved out of the original god-class GeneratorViewModel (M1).
        builder.Services.AddSingleton<IApiTokenStore, ApiTokenStore>();
        builder.Services.AddSingleton<IPollinationsTokenStore, PollinationsTokenStore>();
        builder.Services.AddSingleton<IComfyUiAuthStore, ComfyUiAuthStore>();
        builder.Services.AddSingleton<ICivitaiTokenStore, CivitaiTokenStore>();
        builder.Services.AddSingleton<ICivitaiPostingService, CivitaiPostingService>();
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
        builder.Services.AddTransient<IdeogramStructureEditorViewModel>();
        // Singleton so the mutation run state (seed, count, axis, slot-tag edits) survives a
        // round-trip to MainPage and back — re-initialized only when a genuinely new base loads
        // (same-base detection in InitializeFrom). The page stays transient and binds this one VM.
        builder.Services.AddSingleton<MutationEngineViewModel>();

        // 4) Register MainPage so it (and its constructor) can be injected. See the
        //    Singleton rationale above the GeneratorViewModel registration.
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<GalleryPage>();
        builder.Services.AddTransient<GalleryItemDetailPage>();
        builder.Services.AddTransient<IdeogramStructureEditorPage>();
        builder.Services.AddTransient<MutationEnginePage>();
        // SettingsPage binds the singleton GeneratorViewModel (tokens drive IsValid there);
        // the page itself is cheap to rebuild per navigation.
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();

        // Apply the persisted output-folder setting before any save/gallery use, independent of
        // VM init order. CrashLogger already configured app.log against the fixed default above —
        // intended: the log stays anchored regardless of this setting.
        var savedOutputFolder = app.Services.GetRequiredService<IUiStateStore>().LoadOutputFolder();
        Shared.Constants.OutputPaths.SetGeneratedImagesOverride(savedOutputFolder);

        return app;
    }
}
