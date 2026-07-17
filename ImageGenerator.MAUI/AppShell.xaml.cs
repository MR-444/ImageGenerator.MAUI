using ImageGenerator.MAUI.Presentation.Views;

namespace ImageGenerator.MAUI;

public partial class AppShell
{
    public AppShell()
    {
        InitializeComponent();

        // Register pushable routes so Shell.Current.GoToAsync("gallery") resolves the page
        // through DI. Without this the call would fail with "ambiguous routes" or unresolved.
        Routing.RegisterRoute("detail", typeof(GalleryItemDetailPage));
        Routing.RegisterRoute("ideogram-editor", typeof(IdeogramStructureEditorPage));
        Routing.RegisterRoute("mutation-engine", typeof(MutationEnginePage));
        Routing.RegisterRoute("idea-to-prompt", typeof(IdeaToPromptPage));
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        var version = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppShell).Assembly)
            ?.InformationalVersion
            ?.Split('+')[0]
            ?? "0.0.0";
        var message =
            $"Version {version}\n\n" +
            "A hobby MAUI desktop workbench for image generation via Replicate, Pollinations.ai, and your own ComfyUI server.\n\n" +
            "MIT License\n" +
            "https://github.com/MR-444/ImageGenerator.MAUI";

        await DisplayAlertAsync("About Emberforge", message, "OK");
    }
}
