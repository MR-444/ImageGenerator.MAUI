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
}
