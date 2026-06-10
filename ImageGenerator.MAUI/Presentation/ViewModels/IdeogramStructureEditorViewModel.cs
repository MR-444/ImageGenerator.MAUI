using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.Common;
using ImageGenerator.MAUI.Presentation.Drawing;
using Microsoft.Extensions.Logging;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate.
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Builds/edits an Ideogram V4 structured prompt visually. Two exits from one model:
/// Apply serializes COMPACT into MainPage's prompt box via the Shell query-param hand-off
/// (Replicate's cog wants json_prompt as a string), "Save JSON to file" writes the same model
/// pretty-printed to disk for use outside this app. Transient like GalleryItemDetailViewModel;
/// MainPage's singleton VM state survives the round-trip.
/// </summary>
public partial class IdeogramStructureEditorViewModel : ObservableObject
{
    private readonly IJsonPromptFileService _fileService;
    private readonly IFileLauncher _fileLauncher;
    private readonly ILogger<IdeogramStructureEditorViewModel> _logger;
    private readonly GeneratorViewModel? _generator;

    /// <summary>Raised whenever anything the canvas renders may have changed. The page maps it to GraphicsView.Invalidate().</summary>
    public event Action? CanvasInvalidated;

    /// <summary>
    /// The generator VM is a DI singleton, so taking it here couples only the resolution
    /// write-through: a picker change in the editor must reach Parameters.Resolution
    /// immediately, not just on Apply — leaving via back navigation otherwise discards it.
    /// Optional so unit tests (and any future host) can construct the editor stand-alone.
    /// </summary>
    public IdeogramStructureEditorViewModel(
        IJsonPromptFileService fileService,
        IFileLauncher fileLauncher,
        ILogger<IdeogramStructureEditorViewModel> logger,
        GeneratorViewModel? generator = null)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _fileLauncher = fileLauncher ?? throw new ArgumentNullException(nameof(fileLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;

        Elements.CollectionChanged += OnElementsChanged;
    }

    [ObservableProperty]
    private string _highLevelDescription = string.Empty;

    [ObservableProperty]
    private string _background = string.Empty;

    // Style card. IncludeStyle gates whether style_description is emitted at all; IsPhoto
    // swaps which of the mutually exclusive art_style / photo fields is editable + emitted.
    [ObservableProperty]
    private bool _includeStyle;

    [ObservableProperty]
    private string _aesthetics = string.Empty;

    [ObservableProperty]
    private string _lighting = string.Empty;

    [ObservableProperty]
    private string _medium = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtStyle))]
    private bool _isPhoto;

    public bool IsArtStyle => !IsPhoto;

    [ObservableProperty]
    private string _artStyle = string.Empty;

    [ObservableProperty]
    private string _photoStyle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StylePaletteSwatches))]
    private string _stylePaletteText = string.Empty;

    /// <summary>Renderable chips for the style palette text (unparseable entries skipped).</summary>
    public IReadOnlyList<PaletteSwatch> StylePaletteSwatches => PaletteSwatches.From(StylePaletteText);

    public ObservableCollection<ElementItemViewModel> Elements { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedElement))]
    private ElementItemViewModel? _selectedElement;

    public bool HasSelectedElement => SelectedElement is not null;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportedPath))]
    private string? _exportedPath;

    public bool HasExportedPath => !string.IsNullOrEmpty(ExportedPath);

    // --- Target resolution / canvas shape -------------------------------------------------
    // The bbox grid is always 0–1000 per axis (schema constant), but the canvas mirrors the
    // target image's aspect ratio so boxes preview where they'll actually land. The picker is
    // seeded from the generator's current resolution and handed back on Apply.

    /// <summary>Longest canvas side in device-independent pixels; the other side shrinks to the AR.</summary>
    public const double CanvasFitBox = 480;

    public IReadOnlyList<string> ResolutionOptions { get; } = IdeogramV4Descriptor.AllResolutions;

    [ObservableProperty]
    private string _selectedResolution = IdeogramV4Descriptor.AutoResolution;

    [ObservableProperty]
    private double _canvasWidthRequest = CanvasFitBox;

    [ObservableProperty]
    private double _canvasHeightRequest = CanvasFitBox;

    // Guards the write-through below during SetIncomingResolution: seeding reflects the
    // generator's CURRENT state (or the Auto fallback for unknown values) and must not be
    // written back as if the user picked it — the Auto fallback would otherwise overwrite
    // a saved choice just by opening the editor.
    private bool _suppressGeneratorSync;

    partial void OnSelectedResolutionChanged(string value)
    {
        if (CanvasCoordinateMapper.TryParseResolution(value, out var width, out var height))
        {
            var aspectRatio = (double)width / height;
            CanvasWidthRequest = aspectRatio >= 1 ? CanvasFitBox : CanvasFitBox * aspectRatio;
            CanvasHeightRequest = aspectRatio >= 1 ? CanvasFitBox / aspectRatio : CanvasFitBox;
        }
        else
        {
            // "Auto" (Ideogram picks the AR itself) or anything unparseable -> square preview.
            CanvasWidthRequest = CanvasFitBox;
            CanvasHeightRequest = CanvasFitBox;
        }
        CanvasInvalidated?.Invoke();

        // Write the pick through to the generator immediately so it survives leaving the
        // editor WITHOUT Apply (back navigation has no query-param hand-off). Membership
        // guard mirrors MainPage.IdeogramResolution.
        if (!_suppressGeneratorSync
            && _generator is not null
            && _generator.ResolutionOptions.Contains(value))
        {
            _generator.Parameters.Resolution = value;
        }
    }

    /// <summary>Seeds the picker from the generator's resolution query param; unknown values fall back to Auto.</summary>
    public void SetIncomingResolution(string? resolution)
    {
        _suppressGeneratorSync = true;
        try
        {
            SelectedResolution = resolution is not null && ResolutionOptions.Contains(resolution)
                ? resolution
                : IdeogramV4Descriptor.AutoResolution;
        }
        finally { _suppressGeneratorSync = false; }
    }

    // --- RGB color picker (shared by the style and element palettes) -----------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerHex))]
    [NotifyPropertyChangedFor(nameof(PickerPreviewColor))]
    private int _pickerRed = 181;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerHex))]
    [NotifyPropertyChangedFor(nameof(PickerPreviewColor))]
    private int _pickerGreen = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerHex))]
    [NotifyPropertyChangedFor(nameof(PickerPreviewColor))]
    private int _pickerBlue = 43;

    public string PickerHex => PaletteSwatches.ToHex(PickerRed, PickerGreen, PickerBlue);

    public Color PickerPreviewColor => Color.FromArgb(PickerHex);

    [RelayCommand]
    private void AddPickerColorToStyle()
    {
        if (StylePaletteSwatches.Count >= StyleDescription.MaxPaletteColors)
        {
            SetStatus($"Style palette is full ({StyleDescription.MaxPaletteColors} colors max).", StatusKind.Warning);
            return;
        }
        StylePaletteText = PaletteSwatches.Append(StylePaletteText, PickerHex);
        IncludeStyle = true; // a palette only ships inside style_description
    }

    [RelayCommand]
    private void AddPickerColorToElement()
    {
        if (SelectedElement is null) return;
        if (SelectedElement.Swatches.Count >= Element.MaxPaletteColors)
        {
            SetStatus($"Element palette is full ({Element.MaxPaletteColors} colors max).", StatusKind.Warning);
            return;
        }
        SelectedElement.PaletteText = PaletteSwatches.Append(SelectedElement.PaletteText, PickerHex);
    }

    [RelayCommand]
    private void RemoveStyleColor(string hex) =>
        StylePaletteText = PaletteSwatches.RemoveFirst(StylePaletteText, hex);

    [RelayCommand]
    private void RemoveElementColor(string hex)
    {
        if (SelectedElement is null) return;
        SelectedElement.PaletteText = PaletteSwatches.RemoveFirst(SelectedElement.PaletteText, hex);
    }

    partial void OnSelectedElementChanged(ElementItemViewModel? value) => CanvasInvalidated?.Invoke();

    private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Track per-item edits (bbox sliders, desc) so the canvas stays live while typing.
        if (e.OldItems is not null)
            foreach (ElementItemViewModel item in e.OldItems) item.PropertyChanged -= OnElementItemChanged;
        if (e.NewItems is not null)
            foreach (ElementItemViewModel item in e.NewItems) item.PropertyChanged += OnElementItemChanged;
        CanvasInvalidated?.Invoke();
    }

    private void OnElementItemChanged(object? sender, PropertyChangedEventArgs e) => CanvasInvalidated?.Invoke();

    /// <summary>
    /// Seeds the editor from the prompt box content handed over via the Shell route. Anything
    /// that isn't a parseable structured prompt (plain-text prompt, garbage, empty) starts a
    /// fresh template — never throws, the user just gets a status note.
    /// </summary>
    public void LoadFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            SetStatus("Building a new structured prompt.", StatusKind.Info);
            return;
        }

        try
        {
            var model = V4JsonPromptSerializer.Deserialize(json);
            HighLevelDescription = model.HighLevelDescription;
            Background = model.CompositionalDeconstruction?.Background ?? string.Empty;

            if (model.StyleDescription is { } style)
            {
                IncludeStyle = true;
                Aesthetics = style.Aesthetics ?? string.Empty;
                Lighting = style.Lighting ?? string.Empty;
                Medium = style.Medium ?? string.Empty;
                IsPhoto = !string.IsNullOrWhiteSpace(style.Photo);
                ArtStyle = style.ArtStyle ?? string.Empty;
                PhotoStyle = style.Photo ?? string.Empty;
                StylePaletteText = style.ColorPalette is { Count: > 0 } ? string.Join(", ", style.ColorPalette) : string.Empty;
            }

            foreach (var element in model.CompositionalDeconstruction?.Elements ?? [])
                Elements.Add(ElementItemViewModel.FromElement(element));

            SelectedElement = Elements.FirstOrDefault();
            SetStatus($"Loaded structured prompt with {Elements.Count} element(s).", StatusKind.Info);
        }
        catch (V4JsonPromptParseException ex)
        {
            _logger.LogInformation(ex, "IdeogramStructureEditorVM.{Op}: prompt box content not parseable, starting fresh", "LoadFromJson");
            SetStatus("Prompt box content isn't a structured prompt — starting fresh.", StatusKind.Warning);
        }
    }

    /// <summary>Assembles the typed model from the editable surface. Pure; never throws on bad input.</summary>
    public V4JsonPrompt BuildModel() => new()
    {
        HighLevelDescription = HighLevelDescription.Trim(),
        StyleDescription = IncludeStyle
            ? new StyleDescription
            {
                Aesthetics = NullIfBlank(Aesthetics),
                Lighting = NullIfBlank(Lighting),
                Medium = NullIfBlank(Medium),
                ArtStyle = IsPhoto ? null : NullIfBlank(ArtStyle),
                Photo = IsPhoto ? NullIfBlank(PhotoStyle) : null,
                ColorPalette = ElementItemViewModel.ParsePalette(StylePaletteText)
            }
            : null,
        CompositionalDeconstruction = new CompositionalDeconstruction
        {
            Background = Background.Trim(),
            Elements = Elements.Select(e => e.ToElement()).ToList()
        }
    };

    /// <summary>Snapshot for the drawable; pulled on every draw via the BoxesProvider hook.</summary>
    public IReadOnlyList<CanvasBox> BuildCanvasBoxes() =>
        Elements
            .Where(e => e.HasBbox)
            .Select(e => new CanvasBox(
                [e.YMin, e.XMin, e.YMax, e.XMax],
                e.Summary,
                e.IsText,
                ReferenceEquals(e, SelectedElement)))
            .ToList();

    /// <summary>Tap-to-select: picks the topmost (last-added) element whose bbox contains the tap.</summary>
    public void SelectElementAt(float pixelX, float pixelY, float canvasWidth, float canvasHeight)
    {
        var gridX = CanvasCoordinateMapper.PixelsToGrid(pixelX, canvasWidth);
        var gridY = CanvasCoordinateMapper.PixelsToGrid(pixelY, canvasHeight);

        var hit = Elements
            .Where(e => e.HasBbox)
            .LastOrDefault(e => CanvasCoordinateMapper.BboxContains([e.YMin, e.XMin, e.YMax, e.XMax], gridX, gridY));

        if (hit is not null) SelectedElement = hit;
    }

    [RelayCommand]
    private void AddObjElement() => AddElement(Element.ObjType);

    [RelayCommand]
    private void AddTextElement() => AddElement(Element.TextType);

    private void AddElement(string type)
    {
        // Stagger default boxes so consecutive adds don't stack invisibly on top of each other.
        var offset = Math.Min(Elements.Count * 40, 200);
        var item = new ElementItemViewModel(type)
        {
            HasBbox = true,
            YMin = 250 + offset,
            XMin = 250 + offset,
            YMax = Math.Min(750 + offset, V4JsonPrompt.CanvasSize),
            XMax = Math.Min(750 + offset, V4JsonPrompt.CanvasSize)
        };
        Elements.Add(item);
        SelectedElement = item;
    }

    [RelayCommand]
    private void DeleteSelectedElement()
    {
        if (SelectedElement is null) return;
        var index = Elements.IndexOf(SelectedElement);
        Elements.Remove(SelectedElement);
        SelectedElement = Elements.Count > 0 ? Elements[Math.Min(index, Elements.Count - 1)] : null;
    }

    /// <summary>
    /// Validate → serialize compact → hand the string back to MainPage. Blocks on validation
    /// errors so an incomplete model can never reach the prompt box (the 422 stays dead).
    /// </summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        var model = BuildModel();
        if (!ReportValidation(model)) return;

        var compact = V4JsonPromptSerializer.Serialize(model);
        try
        {
            await Shell.Current.GoToAsync(BuildApplyRoute(compact));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorVM.{Op}", "Apply");
            SetStatus($"Couldn't return to the generator: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorVM.{Op}", "Cancel");
        }
    }

    [RelayCommand]
    private async Task SaveToFileAsync()
    {
        var model = BuildModel();
        if (!ReportValidation(model)) return;

        try
        {
            var pretty = V4JsonPromptSerializer.Serialize(model, indented: true);
            ExportedPath = await _fileService.SaveAsync(model.HighLevelDescription, pretty);
            SetStatus($"Saved: {ExportedPath}", StatusKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorVM.{Op}", "SaveToFile");
            SetStatus($"Couldn't save the JSON file: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private void ShowExportInFolder()
    {
        try
        {
            if (string.IsNullOrEmpty(ExportedPath)) return;
            _fileLauncher.RevealInFolder(ExportedPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorVM.{Op}", "ShowExportInFolder");
        }
    }

    [RelayCommand]
    private void SelectElement(ElementItemViewModel item) => SelectedElement = item;

    /// <summary>
    /// The Apply hand-off route: compact JSON plus the resolution chosen on the canvas card,
    /// both consumed by MainPage QueryProperties. Internal so tests can pin the shape without Shell.
    /// </summary>
    internal string BuildApplyRoute(string compactJson) =>
        $"//MainPage?ideogramJson={Uri.EscapeDataString(compactJson)}" +
        $"&ideogramResolution={Uri.EscapeDataString(SelectedResolution)}";

    private bool ReportValidation(V4JsonPrompt model)
    {
        var errors = V4JsonPromptValidator.Validate(model);
        if (errors.Count == 0) return true;

        SetStatus(string.Join(Environment.NewLine, errors), StatusKind.Error);
        return false;
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
