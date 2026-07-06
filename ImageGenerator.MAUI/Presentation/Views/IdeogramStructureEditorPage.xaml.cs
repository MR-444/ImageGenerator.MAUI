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
    private WorkspaceLayoutMode _workspaceLayoutMode = WorkspaceLayoutMode.Wide;

    private enum WorkspaceLayoutMode { Wide, Medium, Narrow }

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

    // The view->VM half of the palette Entries' split bindings (see the XAML comments) —
    // explicit pushes survive the WinUI TwoWay-binding dropout, mirroring
    // MainPage.OnPromptTextChanged; the [ObservableProperty] equality check suppresses echo loops.
    private void OnStylePaletteTextChanged(object? sender, TextChangedEventArgs e) =>
        _viewModel.StylePaletteText = e.NewTextValue ?? string.Empty;

    private void OnElementPaletteTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel.SelectedElement is { } element)
            element.PaletteText = e.NewTextValue ?? string.Empty;
    }

    // The "Examples" pickers insert a doc-sourced phrase into their Entry, then reset so the
    // picker keeps showing its title and the same suggestion can be re-picked. The reset
    // re-fires SelectedIndexChanged with a null SelectedItem, which the type guard ignores.
    private void OnMediumSuggestionPicked(object? sender, EventArgs e) =>
        InsertSuggestion(sender, v => _viewModel.Medium = v);

    private void OnAestheticsSuggestionPicked(object? sender, EventArgs e) =>
        InsertSuggestion(sender, v => _viewModel.Aesthetics = v);

    private void OnLightingSuggestionPicked(object? sender, EventArgs e) =>
        InsertSuggestion(sender, v => _viewModel.Lighting = v);

    private void OnArtStyleSuggestionPicked(object? sender, EventArgs e) =>
        InsertSuggestion(sender, v => _viewModel.ArtStyle = v);

    private void OnPhotoStyleSuggestionPicked(object? sender, EventArgs e) =>
        InsertSuggestion(sender, v => _viewModel.PhotoStyle = v);

    private static void InsertSuggestion(object? sender, Action<string> apply)
    {
        if (sender is not Picker { SelectedItem: string value } picker) return;
        apply(value);
        picker.SelectedIndex = -1;
    }

    // Accordion sections in the inspector pane. The header's TapGestureRecognizer passes its
    // section body (via CommandParameter/x:Reference) and we flip its visibility; the header
    // chevron follows the body's IsVisible through BoolToChevronConverter, so no VM state is
    // involved.
    private void OnSectionHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is View body)
            body.IsVisible = !body.IsVisible;
    }

    private void OnCanvasStageSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not VisualElement stage || stage.Width <= 0 || stage.Height <= 0) return;

        const double horizontalPaddingAndBreathingRoom = 56;
        const double verticalChrome = 150;
        var fitBox = Math.Min(stage.Width - horizontalPaddingAndBreathingRoom, stage.Height - verticalChrome);
        _viewModel.SetCanvasFitBox(fitBox);
    }

    private void OnWorkspaceSizeChanged(object? sender, EventArgs e)
    {
        if (WorkspaceGrid.Width <= 0) return;

        var mode = WorkspaceGrid.Width >= 1180
            ? WorkspaceLayoutMode.Wide
            : WorkspaceGrid.Width >= 720
                ? WorkspaceLayoutMode.Medium
                : WorkspaceLayoutMode.Narrow;
        if (mode == _workspaceLayoutMode) return;

        _workspaceLayoutMode = mode;
        switch (mode)
        {
            case WorkspaceLayoutMode.Wide:
                ApplyWideWorkspace();
                break;
            case WorkspaceLayoutMode.Medium:
                ApplyMediumWorkspace();
                break;
            case WorkspaceLayoutMode.Narrow:
                ApplyNarrowWorkspace();
                break;
        }
    }

    private void ApplyWideWorkspace()
    {
        // Proportional columns so the inspector/output panes widen with the window instead
        // of staying pinned to a narrow fixed width; the canvas is centred in its pane so a
        // star centre column is fine. MinimumWidthRequest on the panes (XAML) stops them
        // collapsing before the Narrow breakpoint takes over.
        SetColumns(new GridLength(1.3, GridUnitType.Star), new GridLength(1.6, GridUnitType.Star), new GridLength(1.3, GridUnitType.Star));
        SetRows(new GridLength(1, GridUnitType.Star));
        WorkspaceGrid.ColumnSpacing = 16;
        WorkspaceGrid.RowSpacing = 16;

        PlacePane(InspectorPane, row: 0, column: 0);
        PlacePane(CanvasStage, row: 0, column: 1);
        PlacePane(OutputPane, row: 0, column: 2);
    }

    private void ApplyMediumWorkspace()
    {
        SetColumns(new GridLength(1, GridUnitType.Star), new GridLength(1.4, GridUnitType.Star));
        SetRows(new GridLength(1, GridUnitType.Star), new GridLength(300));
        WorkspaceGrid.ColumnSpacing = 16;
        WorkspaceGrid.RowSpacing = 16;

        PlacePane(InspectorPane, row: 0, column: 0, rowSpan: 2);
        PlacePane(CanvasStage, row: 0, column: 1);
        PlacePane(OutputPane, row: 1, column: 1);
    }

    private void ApplyNarrowWorkspace()
    {
        SetColumns(new GridLength(1, GridUnitType.Star));
        SetRows(new GridLength(340), new GridLength(1, GridUnitType.Star), new GridLength(280));
        WorkspaceGrid.ColumnSpacing = 0;
        WorkspaceGrid.RowSpacing = 16;

        PlacePane(InspectorPane, row: 0, column: 0);
        PlacePane(CanvasStage, row: 1, column: 0);
        PlacePane(OutputPane, row: 2, column: 0);
    }

    private void SetColumns(params GridLength[] widths)
    {
        WorkspaceGrid.ColumnDefinitions.Clear();
        foreach (var width in widths)
            WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
    }

    private void SetRows(params GridLength[] heights)
    {
        WorkspaceGrid.RowDefinitions.Clear();
        foreach (var height in heights)
            WorkspaceGrid.RowDefinitions.Add(new RowDefinition { Height = height });
    }

    private static void PlacePane(BindableObject pane, int row, int column, int rowSpan = 1)
    {
        Grid.SetRow(pane, row);
        Grid.SetColumn(pane, column);
        Grid.SetRowSpan(pane, rowSpan);
        Grid.SetColumnSpan(pane, 1);
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
