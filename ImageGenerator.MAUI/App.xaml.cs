namespace ImageGenerator.MAUI;

public partial class App
{
    public App()
    {
        InitializeComponent();
    }
    
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Width = 1200,
            Height = 900,
            MinimumWidth = 800,
            MinimumHeight = 600
        };
        return window;
    }
}