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
public partial class MainPage
{
    private readonly GeneratorViewModel _viewModel;
    private readonly ILogger<MainPage> _logger;

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
            // Fire-and-forget: AddAsInputAsync sets a status message internally and never
            // throws past its own catch. Awaiting from a property setter isn't possible.
            _ = _viewModel.InputImages.AddAsInputAsync(value);
        }
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };

    public MainPage(GeneratorViewModel viewModel, ILogger<MainPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        BindingContext = viewModel;

        // Activation forensics: a generation once started without any conscious click
        // (app.log 2026-06-10 22:51:38). These platform hooks classify every "Generate
        // activated" line as pointer vs keyboard vs neither (= programmatic).
        _generatePointerPressedHandler = new Microsoft.UI.Xaml.Input.PointerEventHandler(OnGenerateButtonPointerPressed);
        GenerateButton.HandlerChanged += OnGenerateButtonHandlerChanged;
    }

    // --- Generate-button activation instrumentation (diagnostics only, never sets Handled) ---

    private readonly Microsoft.UI.Xaml.Input.PointerEventHandler _generatePointerPressedHandler;
    private Microsoft.UI.Xaml.Controls.Button? _generatePlatformButton;

    private void OnGenerateButtonHandlerChanged(object? sender, EventArgs e)
    {
        // Detach from any previous platform button on handler swaps so logs never double.
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
        // WinUI's Button marks PointerPressed as handled internally — only
        // handledEventsToo:true ever sees it; a plain += never fires.
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
        // async void: callees swallow internally today, but a future refactor that lets one
        // through would crash via SynchronizationContext. Keep a defensive net here.
        try
        {
            await _viewModel.LoadAllTokensAsync();
            await _viewModel.LoadCachedCatalogAsync();
            _viewModel.LoadSavedUiState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MainPage.{Op} failed", "OnAppearing");
        }
    }

    // The view->VM half of the prompt Editor's split binding (see the XAML comment). Pushing
    // the platform text explicitly survives the WinUI TwoWay-binding dropout seen after
    // paste/clear/paste; the [ObservableProperty] equality check suppresses echo loops.
    private void OnPromptTextChanged(object? sender, TextChangedEventArgs e)
    {
        _viewModel.Parameters.Prompt = e.NewTextValue ?? string.Empty;
    }

    // Same split-binding pattern for the API-token Entry. Provider switches are safe: the
    // Picker swaps SelectedTokenProvider BEFORE the binding rewrites the Entry text, so this
    // echoes the new provider's own value (the [ObservableProperty] equality check stops it).
    private void OnTokenTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel.SelectedTokenProvider is { } provider)
            provider.Value = e.NewTextValue ?? string.Empty;
    }

    // Code-behind because RelativeSource lookups inside a CollectionView.ItemTemplate
    // don't reliably cross the DataTemplate scope under MAUI's compiled bindings.
    private void OnRemoveImageClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: InputImagesCoordinator.InputImageItem item })
        {
            _viewModel.InputImages.RemoveImageCommand.Execute(item);
        }
    }

    // Batch-from-textfile entry point. Code-behind orchestrates because DisplayAlertAsync
    // belongs to the Page (the VM owns the FilePicker call and the parser, both of which
    // are independently testable).
    private async void OnImportPromptsClicked(object sender, EventArgs e)
    {
        try
        {
            var prompts = await _viewModel.Batch.PickAndParsePromptsAsync();
            if (prompts is null || prompts.Count == 0) return;

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

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        var message =
            $"Version {_viewModel.AppVersion}\n\n" +
            "A hobby MAUI desktop app for image generation via Replicate, Pollinations.ai, and your own ComfyUI server.\n\n" +
            "MIT License\n" +
            "https://github.com/MR-444/ImageGenerator.MAUI";
        await DisplayAlertAsync("About AI Image Generator", message, "OK");
    }

    // Drag-and-drop for image-prompt input. MAUI's cross-platform DropGestureRecognizer
    // surfaces the gesture, but file paths require the WinUI DragEventArgs.DataView seen
    // through DropEventArgs.PlatformArgs.
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