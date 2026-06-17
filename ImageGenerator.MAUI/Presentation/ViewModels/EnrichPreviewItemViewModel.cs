namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// One read-only row in the region-enrichment preview: the element's identity plus its original and
/// proposed <c>desc</c>, so the user can compare before accepting. Display-only — Accept copies the new
/// desc onto the live element by index, this record never edits anything.
/// </summary>
public sealed record EnrichPreviewItemViewModel(int Index, string Summary, string OriginalDesc, string NewDesc)
{
    /// <summary>"#1  [obj] champagne bottle" — the row heading.</summary>
    public string Heading => $"#{Index + 1}  {Summary}";
}
