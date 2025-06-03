namespace ImageGenerator.MAUI;

/// <summary>
/// Represents the primary application class for the ImageGenerator.MAUI application.
/// This class is responsible for initializing the application components and providing the main application window.
/// </summary>
public partial class App
{
    /// <summary>
    /// Represents the main application class for the ImageGenerator.MAUI project.
    /// </summary>
    /// <remarks>
    /// This class is responsible for initializing the application and configuring the main window properties such as size and minimum dimensions.
    /// </remarks>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates a new application window with specified dimensions and constraints.
    /// </summary>
    /// <param name="activationState">The activation state of the application, used during window initialization.</param>
    /// <returns>A new instance of the <see cref="Window"/> configured with specified properties.</returns>
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