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
    private readonly ICivitaiTokenStore _civitaiTokenStore;
    private readonly IOpenRouterTokenStore _openRouterTokenStore;

    public App(
        IUiStateStore uiStateStore,
        IApiTokenStore apiTokenStore,
        IPollinationsTokenStore pollinationsTokenStore,
        IComfyUiAuthStore comfyUiAuthStore,
        ICivitaiTokenStore civitaiTokenStore,
        IOpenRouterTokenStore openRouterTokenStore)
    {
        InitializeComponent();
        _uiStateStore = uiStateStore;
        _apiTokenStore = apiTokenStore;
        _pollinationsTokenStore = pollinationsTokenStore;
        _comfyUiAuthStore = comfyUiAuthStore;
        _civitaiTokenStore = civitaiTokenStore;
        _openRouterTokenStore = openRouterTokenStore;

        // Apply the saved color theme before the first window paints, so a forced Light/Dark pick
        // takes effect immediately on launch rather than flashing the OS theme first. Default
        // Unspecified = follow OS (the app's original behavior); the Settings "Appearance" picker
        // writes through here via IUiStateStore. The whole UI is AppThemeBinding-driven.
        UserAppTheme = _uiStateStore.LoadAppTheme();
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
            // Sets the OS window caption (title bar / Alt-Tab / taskbar hover). Also makes the
            // dev "empty MainWindowTitle ⇒ zombie process holding the exe lock" check reliable —
            // a live window now always reports a non-empty title.
            Title = "Emberforge",
            MinimumWidth = 900,
            MinimumHeight = 600
        };

        // Own the entire caption through MAUI instead of mixing Shell's extended drag region with
        // AppWindow button colors. The mixed approach left a white center strip and recolored caption
        // buttons whenever Shell activated a new route. AppTheme bindings update this one surface in
        // place when Settings changes between System, Light, and Dark.
        var studioTitleBar = new TitleBar
        {
            Title = "Emberforge",
            HeightRequest = 40
        };
        studioTitleBar.SetAppThemeColor(
            VisualElement.BackgroundColorProperty,
            Color.FromArgb("#F7F8FB"),
            Color.FromArgb("#17191E"));
        studioTitleBar.SetAppThemeColor(
            TitleBar.ForegroundColorProperty,
            Color.FromArgb("#212121"),
            Color.FromArgb("#FFFFFF"));
        window.TitleBar = studioTitleBar;

        // Restore the last window bounds; first launch fills ~90% of the screen (the old
        // fixed 1500x1000 overflowed shorter screens, e.g. an ultrawide 1440p at 150%
        // scale, while wasting its width). All values are DIPs. DeviceDisplay reports the
        // full screen, not the work area, so reserve a taskbar allowance.
        var display = DeviceDisplay.Current.MainDisplayInfo;
        var screenWidth = display.Width / display.Density;
        var screenHeight = display.Height / display.Density;
        const double taskbarAllowance = 48;
        var usableHeight = Math.Max(600, screenHeight - taskbarAllowance);

        if (_uiStateStore.LoadWindowBounds() is { } b)
        {
            // Clamp: the monitor (or DPI scale) may have changed since last run. Closing
            // while maximized persists the maximized dimensions as a normal-state size —
            // acceptable; the clamp keeps it on-screen.
            window.Width = Math.Clamp(b.Width, window.MinimumWidth, screenWidth);
            window.Height = Math.Clamp(b.Height, window.MinimumHeight, usableHeight);
            window.X = Math.Clamp(b.X, 0, Math.Max(0, screenWidth - window.Width));
            window.Y = Math.Clamp(b.Y, 0, Math.Max(0, usableHeight - window.Height));
        }
        else
        {
            window.Width = Math.Max(window.MinimumWidth, screenWidth * 0.90);
            window.Height = Math.Max(window.MinimumHeight, usableHeight * 0.90);
            window.X = Math.Max(0, (screenWidth - window.Width) / 2);
            window.Y = Math.Max(0, (usableHeight - window.Height) / 2);
        }

        // Several instances may share one app.log; the shutdown line pairs with "startup OK"
        // so the log shows which instances were alive when. Flush the debounced writers
        // first: a prompt or token scheduled within the 500 ms window of closing would
        // otherwise never reach storage.
        window.Destroying += (_, _) =>
        {
            _uiStateStore.PersistWindowBounds(window.Width, window.Height, window.X, window.Y);
            _uiStateStore.FlushPendingWrites();
            _apiTokenStore.FlushPendingWrites();
            _pollinationsTokenStore.FlushPendingWrites();
            _comfyUiAuthStore.FlushPendingWrites();
            _civitaiTokenStore.FlushPendingWrites();
            _openRouterTokenStore.FlushPendingWrites();
            CrashLogger.WriteShutdownLine();
        };

        return window;
    }
}
