using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

// DataPackageOperation collides between MAUI (cross-platform) and WinRT (platform). The
// MAUI DragEventArgs.AcceptedOperation expects the MAUI type; the WinUI DragEventArgs
// inside PlatformArgs expects the WinRT type.
using MauiDataPackageOperation = Microsoft.Maui.Controls.DataPackageOperation;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;

namespace ImageGenerator.MAUI.Presentation.Views;

// The structure editor's Apply hands its JSON to the generator VM DIRECTLY (see
// IdeogramStructureEditorViewModel.ApplyToGenerator) — never via a QueryProperty here.
// Shell re-applies a page's string query parameters on every back navigation, so a
// "//MainPage?ideogramJson=…" hand-off resurrected the stale JSON each time the user
// backed out of the editor, stomping whatever was in the prompt box.
[QueryProperty(nameof(AddInputPath), "addInput")]
[QueryProperty(nameof(RemixFromPath), "remixFrom")]
[QueryProperty(nameof(MutateFromPath), "mutateFrom")]
public partial class MainPage
{
    private readonly GeneratorViewModel _viewModel;
    private readonly ILogger<MainPage> _logger;
    private MainWorkspaceMode _workspaceMode = MainWorkspaceMode.Wide;

    private enum MainWorkspaceMode { Wide, Medium, Narrow }

    /// <summary>
    /// Set by Shell when the user navigates back from the gallery detail page via the
    /// "Use as input" button. Passed as single-use ShellNavigationQueryParameters (raw value,
    /// no URL decoding) so back navigation can't re-apply it and resurrect a removed image.
    /// </summary>
    public string? AddInputPath
    {
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _ = _viewModel.InputImages.AddAsInputAsync(value);
        }
    }

    /// <summary>
    /// Set by Shell when the user hits "Remix" on the gallery detail page.
    /// </summary>
    public string? RemixFromPath
    {
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _ = _viewModel.RemixFromImageAsync(value);
        }
    }

    /// <summary>
    /// Set by Shell when the user hits "Mutate from this" on the gallery detail page.
    /// </summary>
    public string? MutateFromPath
    {
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _ = _viewModel.Navigation.MutateFromImageAsync(value);
        }
    }

    private static readonly HashSet<string> ImageExtensions =
    [
        with(StringComparer.OrdinalIgnoreCase),
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    ];

    public MainPage(GeneratorViewModel viewModel, ILogger<MainPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        BindingContext = viewModel;

        _generatePointerPressedHandler = new Microsoft.UI.Xaml.Input.PointerEventHandler(OnGenerateButtonPointerPressed);
        GenerateButton.HandlerChanged += OnGenerateButtonHandlerChanged;
        HandlerChanged += OnPageHandlerChanged;
    }

    private void OnMainWorkspaceSizeChanged(object? sender, EventArgs e)
    {
        if (MainWorkspaceGrid.Width <= 0) return;

        var mode = MainWorkspaceGrid.Width >= 1150
            ? MainWorkspaceMode.Wide
            : MainWorkspaceGrid.Width >= 700
                ? MainWorkspaceMode.Medium
                : MainWorkspaceMode.Narrow;

        if (mode == _workspaceMode) return;
        _workspaceMode = mode;

        switch (mode)
        {
            case MainWorkspaceMode.Wide:
                ApplyWideWorkspace();
                break;
            case MainWorkspaceMode.Medium:
                ApplyMediumWorkspace();
                break;
            case MainWorkspaceMode.Narrow:
                ApplyNarrowWorkspace();
                break;
        }
    }

    private void ApplyWideWorkspace()
    {
        SetWorkspaceGrid(
            columns: [new GridLength(3, GridUnitType.Star), new GridLength(2, GridUnitType.Star)],
            rows: [GridLength.Star],
            columnSpacing: 16);

        Grid.SetRow(MainSettingsPane, 0); Grid.SetColumn(MainSettingsPane, 0);
        Grid.SetRow(MainResultsPane, 0); Grid.SetColumn(MainResultsPane, 1);
        MainResultsPane.MaximumHeightRequest = double.PositiveInfinity;

        SetSettingsColumns(twoColumns: true);
    }

    private void ApplyMediumWorkspace()
    {
        // Keep the Results / preview pane visible on the right side by collapsing the
        // Settings pane from 2 card columns down to 1 scrollable column.
        SetWorkspaceGrid(
            columns: [new GridLength(1.15, GridUnitType.Star), new GridLength(1, GridUnitType.Star)],
            rows: [GridLength.Star],
            columnSpacing: 16);

        Grid.SetRow(MainSettingsPane, 0); Grid.SetColumn(MainSettingsPane, 0);
        Grid.SetRow(MainResultsPane, 0); Grid.SetColumn(MainResultsPane, 1);
        MainResultsPane.MaximumHeightRequest = double.PositiveInfinity;

        SetSettingsColumns(twoColumns: false);
    }

    private void ApplyNarrowWorkspace()
    {
        // Row 1 is Auto (0px when Queue.HasJobs is false, capped at 360px when jobs exist) so
        // Settings never gets squeezed into 50% height above an empty black void.
        SetWorkspaceGrid(
            columns: [GridLength.Star],
            rows: [GridLength.Star, GridLength.Auto],
            columnSpacing: 0);

        Grid.SetRow(MainSettingsPane, 0); Grid.SetColumn(MainSettingsPane, 0);
        Grid.SetRow(MainResultsPane, 1); Grid.SetColumn(MainResultsPane, 0);
        MainResultsPane.MaximumHeightRequest = 360;

        SetSettingsColumns(twoColumns: false);
    }

    private void SetWorkspaceGrid(GridLength[] columns, GridLength[] rows, double columnSpacing)
    {
        MainWorkspaceGrid.ColumnDefinitions.Clear();
        foreach (var col in columns)
            MainWorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(col));

        MainWorkspaceGrid.RowDefinitions.Clear();
        foreach (var row in rows)
            MainWorkspaceGrid.RowDefinitions.Add(new RowDefinition(row));

        MainWorkspaceGrid.ColumnSpacing = columnSpacing;
    }

    private void SetSettingsColumns(bool twoColumns)
    {
        SettingsCardsGrid.ColumnDefinitions.Clear();
        SettingsCardsGrid.RowDefinitions.Clear();

        if (twoColumns)
        {
            SettingsCardsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SettingsCardsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SettingsCardsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SettingsCardsGrid.ColumnSpacing = 16;

            Grid.SetRow(SettingsCol0, 0); Grid.SetColumn(SettingsCol0, 0);
            Grid.SetRow(SettingsCol1, 0); Grid.SetColumn(SettingsCol1, 1);
        }
        else
        {
            SettingsCardsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            SettingsCardsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SettingsCardsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            SettingsCardsGrid.ColumnSpacing = 0;

            Grid.SetRow(SettingsCol0, 0); Grid.SetColumn(SettingsCol0, 0);
            Grid.SetRow(SettingsCol1, 1); Grid.SetColumn(SettingsCol1, 0);
        }
    }

    // --- Ctrl+Enter = Generate (page-root tunnel handler) ---

    private Microsoft.UI.Xaml.UIElement? _pageRoot;

    private void OnPageHandlerChanged(object? sender, EventArgs e)
    {
        if (_pageRoot is not null)
        {
            _pageRoot.PreviewKeyDown -= OnPagePreviewKeyDown;
            _pageRoot = null;
        }
        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement root)
        {
            _pageRoot = root;
            root.PreviewKeyDown += OnPagePreviewKeyDown;
        }
    }

    private void OnPagePreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!ctrl) return;
        e.Handled = true;
        _logger.LogInformation("Generate hotkey Ctrl+Enter IsValid={IsValid}", _viewModel.IsValid);
        if (!_viewModel.IsValid) return;
        _viewModel.GenerateImageCommand.Execute(null);
    }

    // --- Generate-button activation instrumentation (diagnostics only, never sets Handled) ---

    private readonly Microsoft.UI.Xaml.Input.PointerEventHandler _generatePointerPressedHandler;
    private Microsoft.UI.Xaml.Controls.Button? _generatePlatformButton;

    private void OnGenerateButtonHandlerChanged(object? sender, EventArgs e)
    {
        if (_generatePlatformButton is not null)
        {
            _generatePlatformButton.RemoveHandler(
                Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _generatePointerPressedHandler);
            _generatePlatformButton.PreviewKeyDown -= OnGenerateButtonPreviewKeyDown;
            _generatePlatformButton.GotFocus -= OnGenerateButtonGotFocus;
            _generatePlatformButton = null;
        }

        if (GenerateButton.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Button platform) return;
        _generatePlatformButton = platform;
        platform.AddHandler(
            Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _generatePointerPressedHandler, handledEventsToo: true);
        platform.PreviewKeyDown += OnGenerateButtonPreviewKeyDown;
        platform.GotFocus += OnGenerateButtonGotFocus;
    }

    private void OnGenerateButtonPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => _logger.LogInformation("GenerateButton PointerPressed Device={Device}", e.Pointer.PointerDeviceType);

    private void OnGenerateButtonPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _logger.LogInformation("GenerateButton PreviewKeyDown Key={Key}", e.Key);

    private void OnGenerateButtonGotFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => _logger.LogDebug("GenerateButton GotFocus State={State}", _generatePlatformButton?.FocusState);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.Queue.LoadTotalImagesGeneratedAsync();
        try
        {
            await _viewModel.Tokens.LoadAllAsync();
            await _viewModel.LoadCachedCatalogAsync();
            _viewModel.LoadSavedUiState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MainPage.{Op} failed", "OnAppearing");
        }
    }

    private void OnPromptTextChanged(object? sender, TextChangedEventArgs e)
    {
        _viewModel.Parameters.Prompt = e.NewTextValue ?? string.Empty;
    }

    private void OnRemoveImageClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: InputImagesCoordinator.InputImageItem item })
        {
            _viewModel.InputImages.RemoveImageCommand.Execute(item);
        }
    }

    private async void OnImportPromptsClicked(object? sender, EventArgs e)
    {
        try
        {
            var prompts = await _viewModel.Batch.PickAndParsePromptsAsync();
            // Null on cancel or error (an empty file is reported and returns null too).
            if (prompts is null) return;

            var modelName = _viewModel.ProviderFilter.SelectedModel?.Display ?? _viewModel.Parameters.Model;
            var confirm = await DisplayAlertAsync(
                "Run batch?",
                $"Submit {prompts.Count} prompts using {modelName}?\n\nCurrent settings (aspect ratio, format, etc.) will apply to every prompt.",
                "Run", "Cancel");
            if (!confirm) return;

            await _viewModel.Batch.RunBatchAsync(prompts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MainPage.{Op} failed", "OnImportPromptsClicked");
            _viewModel.StatusMessage = $"Batch failed: {ex.Message}";
            _viewModel.StatusKind = StatusKind.Error;
        }
    }

    private void OnImageDragOver(object? sender, DragEventArgs e)
    {
        var winArgs = e.PlatformArgs?.DragEventArgs;
        if (winArgs?.DataView is { } dv && dv.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = MauiDataPackageOperation.Copy;
            winArgs.AcceptedOperation = WinDataPackageOperation.Copy;
            winArgs.DragUIOverride.Caption = "Add as input image";
        }
    }

    private async void OnImageDropped(object? sender, DropEventArgs e)
    {
        var dv = e.PlatformArgs?.DragEventArgs?.DataView;
        if (dv is null || !dv.Contains(StandardDataFormats.StorageItems)) return;

        try
        {
            var items = await dv.GetStorageItemsAsync();
            var imagePaths = items.OfType<StorageFile>()
                                  .Select(f => f.Path)
                                  .Where(p => ImageExtensions.Contains(Path.GetExtension(p)))
                                  .ToList();
            var skipped = items.Count - imagePaths.Count;

            foreach (var path in imagePaths)
            {
                await _viewModel.InputImages.AddAsInputAsync(path);
            }

            if (skipped > 0)
            {
                _viewModel.StatusMessage = imagePaths.Count > 0
                    ? $"Added {imagePaths.Count} image(s); skipped {skipped} non-image file(s)."
                    : $"Skipped {skipped} non-image file(s).";
                _viewModel.StatusKind = StatusKind.Warning;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MainPage.{Op} failed", "OnImageDropped");
        }
    }
}
