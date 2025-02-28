using ImageGenerator.MAUI.Services;
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
		// Register your services and VM
		builder.Services.AddSingleton<IImageGenerationService, ReplicateImageGenerationService>();
		builder.Services.AddTransient<GeneratorViewModel>();

		return builder.Build();
	}
}
