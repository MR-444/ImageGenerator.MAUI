using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Models.OpenAi;
using ImageGenerator.MAUI.Models.Replicate;
using ImageGenerator.MAUI.Services;
using ImageGenerator.MAUI.Services.Replicate;
using ImageGenerator.MAUI.Views; 
using ImageGenerator.MAUI.ViewModels;
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
		builder.Services.AddSingleton<IImageGenerationService, ReplicateImageGenerationService>();
		builder.Services.AddTransient<GeneratorViewModel>();

		// 3) Register MainPage so it (and its constructor) can be injected
		builder.Services.AddTransient<MainPage>();
		
		return builder.Build();
	}
}
