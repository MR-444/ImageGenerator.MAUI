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
/// Apply serializes COMPACT and writes it straight into the singleton generator VM's prompt
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

        // Mode snapshot is safe: this VM is transient (fresh per navigation) and the singleton
        // generator's model can't change while the editor is open. On a ComfyUI workflow the
        // output shape is picked via the ResolutionSelector's aspect-ratio combo strings, not
        // Ideogram's "WxH" list — the picker mirrors the generator's AR options then.
        IsAspectRatioMode = generator is not null
                            && Shared.Constants.ModelConstants.ComfyUi.IsId(generator.Parameters.Model);
        ResolutionOptions = IsAspectRatioMode
            ? generator!.AspectRatioOptions.ToList()
            : IdeogramV4Descriptor.AllResolutions;
        if (IsAspectRatioMode && ResolutionOptions.Count > 0)
        {
            // Field write: no change handler fires, so nothing is written back to the generator.
            _selectedResolution = ResolutionOptions[0];
        }

        Elements.CollectionChanged += OnElementsChanged;
    }

    /// <summary>
    /// True when the host model is a ComfyUI workflow: the picker lists aspect-ratio combo
    /// strings ("3:4 (Portrait Standard)") instead of Ideogram resolutions, and picks write
    /// through to the generator's AspectRatio.
    /// </summary>
    public bool IsAspectRatioMode { get; }

    public string ResolutionPickerTitle => IsAspectRatioMode ? "Aspect ratio" : "Target resolution";

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

    // Mirrors ElementItemViewModel.OnPaletteTextChanged: uppercase live so the Entry's
    // OneWay binding rewrites typed lowercase hex; equality stops the re-entrant set.
    partial void OnStylePaletteTextChanged(string value)
    {
        var normalized = value.ToUpperInvariant();
        if (normalized != value) StylePaletteText = normalized;
    }

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

    /// <summary>Ctor-assigned: Ideogram's resolution list, or the generator's AR combos in <see cref="IsAspectRatioMode"/>.</summary>
    public IReadOnlyList<string> ResolutionOptions { get; }

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
        int width, height;
        var parsed = IsAspectRatioMode
            ? CanvasCoordinateMapper.TryParseAspectRatioLabel(value, out width, out height)
            : CanvasCoordinateMapper.TryParseResolution(value, out width, out height);
        if (parsed)
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
        // editor WITHOUT Apply (back navigation carries nothing). Membership guards mirror
        // ApplyToGenerator. In AR mode the pick is an aspect-ratio combo string and lands
        // on Parameters.AspectRatio — never on Resolution.
        if (_suppressGeneratorSync || _generator is null) return;
        if (IsAspectRatioMode)
        {
            if (_generator.AspectRatioOptions.Contains(value))
            {
                _generator.Parameters.AspectRatio = value;
            }
        }
        else if (_generator.ResolutionOptions.Contains(value))
        {
            _generator.Parameters.Resolution = value;
        }
    }

    /// <summary>
    /// Seeds the picker from the generator's resolution (or, in AR mode, aspect-ratio) query
    /// param; unknown values fall back to Auto / the first AR option.
    /// </summary>
    public void SetIncomingResolution(string? resolution)
    {
        _suppressGeneratorSync = true;
        try
        {
            SelectedResolution = resolution is not null && ResolutionOptions.Contains(resolution)
                ? resolution
                : IsAspectRatioMode && ResolutionOptions.Count > 0
                    ? ResolutionOptions[0]
                    : IdeogramV4Descriptor.AutoResolution;
        }
        finally { _suppressGeneratorSync = false; }
    }

    // --- Style-field suggestion lists -------------------------------------------------------
    // Doc-sourced (ideogram-oss/ideogram4 docs/prompting.md examples + developer.ideogram.ai
    // llms-full.txt). Medium is the model's trained vocabulary; the rest are free-form example
    // phrases — the pickers INSERT into the Entries, they never constrain them.

    public IReadOnlyList<string> MediumSuggestions { get; } =
        ["photograph", "illustration", "3d_render", "painting", "graphic_design"];

    public IReadOnlyList<string> ArtStyleSuggestions { get; } =
        ["flat vector illustration, bold outlines",
         "flat vector design, generous whitespace, sans-serif typography",
         "oil painting"];

    public IReadOnlyList<string> PhotoStyleSuggestions { get; } =
        ["35mm, f/1.4, bokeh",
         "shallow depth of field, sharp focus, eye-level, telephoto",
         "wide angle, f/8, long exposure"];

    public IReadOnlyList<string> LightingSuggestions { get; } =
        ["golden hour, rim light, dramatic shadows",
         "golden hour backlighting, warm atmospheric haze",
         "overcast daylight, diffused, soft subtle shadows",
         "low-key, deep shadows",
         "soft natural window light"];

    public IReadOnlyList<string> AestheticsSuggestions { get; } =
        ["moody, cinematic, desaturated",
         "saturated primary colors, rule of thirds, joyful and triumphant",
         "serene, warm, golden hour",
         "minimal, professional, geometric",
         "warm, cozy, nostalgic"];

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
        if (PaletteSwatches.Contains(StylePaletteText, PickerHex))
        {
            SetStatus($"{PickerHex} is already in the style palette.", StatusKind.Warning);
            return;
        }
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
        if (PaletteSwatches.Contains(SelectedElement.PaletteText, PickerHex))
        {
            SetStatus($"{PickerHex} is already in the element palette.", StatusKind.Warning);
            return;
        }
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

    // --- Canvas gestures (Phase 2: drag-move + corner-resize) ------------------------------
    // The page maps GraphicsView Start/Drag/End/CancelInteraction onto these three methods;
    // all state and math live here so the gestures are unit-testable without MAUI.

    /// <summary>Pointer-to-handle hit radius in pixels (pixel space so the AR can't stretch it).</summary>
    public const float HandleTouchRadius = 14f;

    /// <summary>Smallest box edge a resize can produce, in grid units.</summary>
    public const int MinBoxSize = 20;

    private enum CanvasGesture { None, Move, Resize }

    private CanvasGesture _gesture = CanvasGesture.None;
    private ElementItemViewModel? _gestureTarget;
    private BboxCorner _resizeCorner;
    // Move: pointer grid position minus the box's min corner at press time, so the box
    // follows the grab point instead of jumping its origin onto the pointer.
    private int _grabOffsetX;
    private int _grabOffsetY;

    /// <summary>
    /// Press: a corner handle of the SELECTED box wins first (so handles stay grabbable over
    /// an overlapping box's body) and begins a resize; otherwise the topmost (last-added)
    /// containing box is selected and begins a move; empty canvas keeps the selection.
    /// </summary>
    public void CanvasPointerPressed(float pixelX, float pixelY, float canvasWidth, float canvasHeight)
    {
        if (SelectedElement is { HasBbox: true } selected
            && CanvasCoordinateMapper.HitCorner(
                [selected.YMin, selected.XMin, selected.YMax, selected.XMax],
                pixelX, pixelY, canvasWidth, canvasHeight, HandleTouchRadius) is { } corner)
        {
            _gesture = CanvasGesture.Resize;
            _gestureTarget = selected;
            _resizeCorner = corner;
            return;
        }

        var gridX = CanvasCoordinateMapper.PixelsToGrid(pixelX, canvasWidth);
        var gridY = CanvasCoordinateMapper.PixelsToGrid(pixelY, canvasHeight);

        var hit = Elements
            .Where(e => e.HasBbox)
            .LastOrDefault(e => CanvasCoordinateMapper.BboxContains([e.YMin, e.XMin, e.YMax, e.XMax], gridX, gridY));

        if (hit is null) return;

        SelectedElement = hit;
        _gesture = CanvasGesture.Move;
        _gestureTarget = hit;
        _grabOffsetX = gridX - hit.XMin;
        _grabOffsetY = gridY - hit.YMin;
    }

    /// <summary>
    /// Drag: moves or resizes the gesture target. A no-op without an active press — some
    /// platforms surface drag events without one, and stale drags after release must die.
    /// </summary>
    public void CanvasPointerDragged(float pixelX, float pixelY, float canvasWidth, float canvasHeight)
    {
        if (_gesture == CanvasGesture.None || _gestureTarget is null) return;

        var gridX = CanvasCoordinateMapper.PixelsToGrid(pixelX, canvasWidth);
        var gridY = CanvasCoordinateMapper.PixelsToGrid(pixelY, canvasHeight);
        var target = _gestureTarget;

        if (_gesture == CanvasGesture.Move)
        {
            // Preserve the size exactly; clamp the box as a whole onto the grid.
            var boxWidth = target.XMax - target.XMin;
            var boxHeight = target.YMax - target.YMin;
            var newXMin = Math.Clamp(gridX - _grabOffsetX, 0, V4JsonPrompt.CanvasSize - boxWidth);
            var newYMin = Math.Clamp(gridY - _grabOffsetY, 0, V4JsonPrompt.CanvasSize - boxHeight);
            // Order matters: when moving right/down, write the max first so min/max never
            // momentarily invert for the live canvas + sliders.
            if (newXMin >= target.XMin) { target.XMax = newXMin + boxWidth; target.XMin = newXMin; }
            else { target.XMin = newXMin; target.XMax = newXMin + boxWidth; }
            if (newYMin >= target.YMin) { target.YMax = newYMin + boxHeight; target.YMin = newYMin; }
            else { target.YMin = newYMin; target.YMax = newYMin + boxHeight; }
            return;
        }

        // Resize: the dragged corner follows the pointer; the opposite corner stays pinned.
        // Clamps enforce the grid bounds and a minimum edge so corners can't cross over.
        // The Min/Max guards keep the clamp ranges valid for hand-authored boxes that are
        // already smaller than MinBoxSize or pinned at a grid edge.
        var maxForYMin = Math.Max(0, target.YMax - MinBoxSize);
        var maxForXMin = Math.Max(0, target.XMax - MinBoxSize);
        var minForYMax = Math.Min(V4JsonPrompt.CanvasSize, target.YMin + MinBoxSize);
        var minForXMax = Math.Min(V4JsonPrompt.CanvasSize, target.XMin + MinBoxSize);
        switch (_resizeCorner)
        {
            case BboxCorner.TopLeft:
                target.YMin = Math.Clamp(gridY, 0, maxForYMin);
                target.XMin = Math.Clamp(gridX, 0, maxForXMin);
                break;
            case BboxCorner.TopRight:
                target.YMin = Math.Clamp(gridY, 0, maxForYMin);
                target.XMax = Math.Clamp(gridX, minForXMax, V4JsonPrompt.CanvasSize);
                break;
            case BboxCorner.BottomLeft:
                target.YMax = Math.Clamp(gridY, minForYMax, V4JsonPrompt.CanvasSize);
                target.XMin = Math.Clamp(gridX, 0, maxForXMin);
                break;
            case BboxCorner.BottomRight:
                target.YMax = Math.Clamp(gridY, minForYMax, V4JsonPrompt.CanvasSize);
                target.XMax = Math.Clamp(gridX, minForXMax, V4JsonPrompt.CanvasSize);
                break;
        }
    }

    /// <summary>Release/cancel: ends the active gesture (subsequent drags are no-ops).</summary>
    public void CanvasPointerReleased()
    {
        _gesture = CanvasGesture.None;
        _gestureTarget = null;
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
    /// Validate → serialize compact → write straight into the singleton generator VM, then pop.
    /// Blocks on validation errors so an incomplete model can never reach the prompt box (the
    /// 422 stays dead). Deliberately NOT a query-param hand-off: Shell re-applies a page's
    /// string query parameters on every back navigation, so a "//MainPage?ideogramJson=…"
    /// route resurrected the stale JSON whenever the user later backed out of the editor.
    /// </summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        var model = BuildModel();
        if (!ReportValidation(model)) return;

        ApplyToGenerator(V4JsonPromptSerializer.Serialize(model));
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorVM.{Op}", "Apply");
            SetStatus($"Couldn't return to the generator: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>
    /// The Apply hand-off: compact JSON becomes the prompt, the structured-JSON toggle turns
    /// on so the descriptor ships it as json_prompt. Internal so tests can pin the contract
    /// without Shell. In AR mode SelectedResolution is an aspect-ratio combo string that isn't
    /// in ResolutionOptions — the membership guard drops it harmlessly; the live write-through
    /// already delivered the pick.
    /// </summary>
    internal void ApplyToGenerator(string compactJson)
    {
        if (_generator is null) return;
        _generator.Parameters.Prompt = compactJson;
        _generator.Parameters.UseJsonPrompt = true;
        if (_generator.ResolutionOptions.Contains(SelectedResolution))
            _generator.Parameters.Resolution = SelectedResolution;
    }

    /// <summary>
    /// Hand the current model to the mutation engine. Unlike Apply this stashes the <b>typed</b>
    /// model on the generator (not a serialized string) so any per-element <c>Element.SlotTag</c>
    /// survives the hop — the engine reads explicit tags only off the typed base. Validated first
    /// so an incomplete model can't reach a mutation run.
    /// </summary>
    [RelayCommand]
    private async Task MutateAsync()
    {
        if (_generator is null) return;

        var model = BuildModel();
        if (!ReportValidation(model)) return;

        _generator.PendingMutationBase = model;
        try
        {
            await Shell.Current.GoToAsync("mutation-engine");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeogramStructureEditorVM.{Op}", "Mutate");
            SetStatus($"Couldn't open the mutation engine: {ex.Message}", StatusKind.Error);
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
