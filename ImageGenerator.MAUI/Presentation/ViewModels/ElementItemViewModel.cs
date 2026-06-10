using CommunityToolkit.Mvvm.ComponentModel;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Presentation.Common;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate.
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Editable surface for one structured-prompt element. The type (obj/text) is fixed at
/// creation — the editor offers separate "Add object"/"Add text" actions instead of retyping
/// in place, which keeps the text-only fields unambiguous. Bbox values clamp themselves onto
/// the 0–1000 grid so sliders and numeric entries can bind directly.
/// </summary>
public partial class ElementItemViewModel : ObservableObject
{
    public ElementItemViewModel(string type)
    {
        Type = type;
    }

    public string Type { get; }
    public bool IsText => Type == Element.TextType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _desc = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _hasBbox;

    [ObservableProperty]
    private int _yMin = 250;

    [ObservableProperty]
    private int _xMin = 250;

    [ObservableProperty]
    private int _yMax = 750;

    [ObservableProperty]
    private int _xMax = 750;

    /// <summary>Comma/space-separated #RRGGBB values; normalized by <see cref="ParsePalette"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Swatches))]
    private string _paletteText = string.Empty;

    /// <summary>Renderable chips for the current palette text (unparseable entries skipped).</summary>
    public IReadOnlyList<PaletteSwatch> Swatches => PaletteSwatches.From(PaletteText);

    /// <summary>One-line caption for the elements master list and the canvas label.</summary>
    public string Summary
    {
        get
        {
            var body = IsText
                ? string.IsNullOrWhiteSpace(Text) ? Desc : $"“{Text}”"
                : Desc;
            if (string.IsNullOrWhiteSpace(body)) body = "(empty)";
            return $"[{Type}] {body}";
        }
    }

    partial void OnYMinChanged(int value) => ClampIntoGrid(value, v => YMin = v);
    partial void OnXMinChanged(int value) => ClampIntoGrid(value, v => XMin = v);
    partial void OnYMaxChanged(int value) => ClampIntoGrid(value, v => YMax = v);
    partial void OnXMaxChanged(int value) => ClampIntoGrid(value, v => XMax = v);

    private static void ClampIntoGrid(int value, Action<int> assign)
    {
        var clamped = Math.Clamp(value, 0, V4JsonPrompt.CanvasSize);
        if (clamped != value) assign(clamped);
    }

    public Element ToElement() => new()
    {
        Type = Type,
        Desc = NullIfBlank(Desc),
        Text = IsText ? NullIfBlank(Text) : null,
        Bbox = HasBbox ? V4JsonPromptValidator.ClampBbox([YMin, XMin, YMax, XMax]) : null,
        ColorPalette = ParsePalette(PaletteText)
    };

    public static ElementItemViewModel FromElement(Element element)
    {
        var item = new ElementItemViewModel(element.Type)
        {
            Desc = element.Desc ?? string.Empty,
            Text = element.Text ?? string.Empty,
            PaletteText = element.ColorPalette is { Count: > 0 } ? string.Join(", ", element.ColorPalette) : string.Empty
        };
        if (element.Bbox is { Length: 4 } bbox)
        {
            item.HasBbox = true;
            item.YMin = bbox[0];
            item.XMin = bbox[1];
            item.YMax = bbox[2];
            item.XMax = bbox[3];
        }
        return item;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Splits on commas/semicolons/whitespace, uppercases, and prefixes a missing '#' on
    /// 6-char entries. Anything still malformed is kept as-is so the validator can name it.
    /// </summary>
    public static List<string>? ParsePalette(string paletteText)
    {
        if (string.IsNullOrWhiteSpace(paletteText)) return null;

        var colors = paletteText
            .Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .Select(c => c.Length == 6 && !c.StartsWith('#') ? "#" + c : c)
            .ToList();
        return colors.Count > 0 ? colors : null;
    }
}
