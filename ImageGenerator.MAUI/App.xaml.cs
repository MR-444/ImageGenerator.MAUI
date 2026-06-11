using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

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
    private readonly IUiStateStore _uiStateStore;
    private readonly IApiTokenStore _apiTokenStore;
    private readonly IPollinationsTokenStore _pollinationsTokenStore;
    private readonly IComfyUiAuthStore _comfyUiAuthStore;

    public App(
        IUiStateStore uiStateStore,
        IApiTokenStore apiTokenStore,
        IPollinationsTokenStore pollinationsTokenStore,
        IComfyUiAuthStore comfyUiAuthStore)
    {
        InitializeComponent();
        _uiStateStore = uiStateStore;
        _apiTokenStore = apiTokenStore;
        _pollinationsTokenStore = pollinationsTokenStore;
        _comfyUiAuthStore = comfyUiAuthStore;
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
            Width = 1500,
            Height = 1000,
            MinimumWidth = 900,
            MinimumHeight = 600
        };

        var display = DeviceDisplay.Current.MainDisplayInfo;
        var screenWidth = display.Width / display.Density;
        var screenHeight = display.Height / display.Density;
        window.X = Math.Max(0, (screenWidth - window.Width) / 2);
        window.Y = Math.Max(0, (screenHeight - window.Height) / 2);

        // Several instances may share one app.log; the shutdown line pairs with "startup OK"
        // so the log shows which instances were alive when. Flush the debounced writers
        // first: a prompt or token scheduled within the 500 ms window of closing would
        // otherwise never reach storage.
        window.Destroying += (_, _) =>
        {
            _uiStateStore.FlushPendingWrites();
            _apiTokenStore.FlushPendingWrites();
            _pollinationsTokenStore.FlushPendingWrites();
            _comfyUiAuthStore.FlushPendingWrites();
            CrashLogger.WriteShutdownLine();
        };

        return window;
    }
}