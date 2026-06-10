using ImageGenerator.MAUI.Presentation.Common;
using ImageGenerator.MAUI.Presentation.Drawing;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.Views;

[QueryProperty(nameof(IncomingJson), "json")]
[QueryProperty(nameof(IncomingResolution), "resolution")]
public partial class IdeogramStructureEditorPage
{
    private readonly IdeogramStructureEditorViewModel _viewModel;
    private readonly ILogger<IdeogramStructureEditorPage> _logger;
    private string? _pendingJson;
    private string? _pendingResolution;
    private bool _loaded;

    public IdeogramStructureEditorPage(IdeogramStructureEditorViewModel viewModel, ILogger<IdeogramStructureEditorPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        BindingContext = viewModel;

        // The drawable pulls boxes lazily on every draw, so the VM only has to say
        // "something changed" and the page maps that to an Invalidate().
        StructureCanvas.Drawable = new IdeogramCanvasDrawable { BoxesProvider = viewModel.BuildCanvasBoxes };
        viewModel.CanvasInvalidated += () => StructureCanvas.Invalidate();
    }

    /// <summary>
    /// Receives the current prompt box content from the Shell route
    /// (`ideogram-editor?json=…`). Stored until OnAppearing — Shell applies query
    /// attributes before the page shows, and LoadFromJson touches bindable state.
    /// Named IncomingJson (not Json) to dodge XAML-compile name collisions, same
    /// reasoning as GalleryItemDetailPage.NavPath.
    /// </summary>
    public string? IncomingJson
    {
        set => _pendingJson = string.IsNullOrEmpty(value) ? null : Uri.UnescapeDataString(value);
    }

    /// <summary>The generator's current resolution — seeds the canvas-card picker / canvas shape.</summary>
    public string? IncomingResolution
    {
        set => _pendingResolution = string.IsNullOrEmpty(value) ? null : Uri.UnescapeDataString(value);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // The page is transient, but OnAppearing can refire within one lifetime (window
        // re-activation); reloading would duplicate every element row.
        if (_loaded) return;
        _loaded = true;

        try
        {
            _viewModel.SetIncomingResolution(_pendingResolution);
            _viewModel.LoadFromJson(_pendingJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorPage.{Op}", "OnAppearing");
        }
        StructureCanvas.Invalidate();
    }

    // Code-behind for the swatch taps, mirroring MainPage.OnRemoveImageClicked: command
    // bindings inside a DataTemplate don't reliably reach the page VM under compiled bindings.
    private void OnStyleSwatchTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: PaletteSwatch swatch })
            _viewModel.RemoveStyleColorCommand.Execute(swatch.Hex);
    }

    private void OnElementSwatchTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: PaletteSwatch swatch })
            _viewModel.RemoveElementColorCommand.Execute(swatch.Hex);
    }

    private void OnCanvasStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        var touch = e.Touches[0];

        _viewModel.CanvasPointerPressed(touch.X, touch.Y,
            (float)StructureCanvas.Width, (float)StructureCanvas.Height);
    }

    private void OnCanvasDragInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        var touch = e.Touches[0];

        _viewModel.CanvasPointerDragged(touch.X, touch.Y,
            (float)StructureCanvas.Width, (float)StructureCanvas.Height);
    }

    private void OnCanvasEndInteraction(object? sender, TouchEventArgs e) =>
        _viewModel.CanvasPointerReleased();

    private void OnCanvasCancelInteraction(object? sender, EventArgs e) =>
        _viewModel.CanvasPointerReleased();
}
