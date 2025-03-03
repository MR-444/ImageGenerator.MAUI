using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;
using ImageGenerator.MAUI.Views; 
using ImageGenerator.MAUI.ViewModels;
using Microsoft.Extensions.Logging;
using Refit;

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
		// 1) Add the Refit client
		builder.Services
			.AddRefitClient<IReplicateApi>()
			.ConfigureHttpClient(client =>
			{
				// Set the base address for all calls
				client.BaseAddress = new Uri("https://api.replicate.com");
			});
		
		// 2) Register your services and VM
		builder.Services.AddSingleton<IImageGenerationService, ReplicateImageGenerationService>();
		builder.Services.AddTransient<GeneratorViewModel>();

		// 3) Register MainPage so it (and its constructor) can be injected
		builder.Services.AddTransient<MainPage>();
		
		return builder.Build();
	}
}
