using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Application.Services;
using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
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
        // 1) Add the Refit clients
        builder.Services.AddRefitClient<IReplicateApi>("https://api.replicate.com");
        builder.Services.AddRefitClient<IOpenAiApi>("https://api.openai.com");
		
        // 2) Register your services and ViewModels
        builder.Services.AddSingleton<IImageEncoderProvider, ImageEncoderProvider>();
        builder.Services.AddSingleton<IImageFileService, ImageFileService>();
        builder.Services.AddSingleton<IOpenAiImageGenerationService, OpenAiImageGenerationService>();
        builder.Services.AddSingleton<IReplicateImageGenerationService, ReplicateImageGenerationService>();
        builder.Services.AddSingleton<IImageGenerationServiceFactory, ImageGenerationServiceFactory>();
        builder.Services.AddSingleton<IImageGenerationService, ImageGenerationOrchestrator>();
        builder.Services.AddTransient<GeneratorViewModel>();

        // 3) Register MainPage so it (and its constructor) can be injected
        builder.Services.AddTransient<MainPage>();
		
        return builder.Build();
    }
}