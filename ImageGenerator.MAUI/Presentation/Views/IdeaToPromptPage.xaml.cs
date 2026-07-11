using ImageGenerator.MAUI.Presentation.ViewModels;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

using MauiDataPackageOperation = Microsoft.Maui.Controls.DataPackageOperation;
using WinDataPackageView = Windows.ApplicationModel.DataTransfer.DataPackageView;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class IdeaToPromptPage
{
    private WorkspaceLayoutMode _workspaceLayoutMode = WorkspaceLayoutMode.Wide;

    private enum WorkspaceLayoutMode { Wide, Narrow }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };
    private const long MaxReferenceImageBytes = 20L * 1024 * 1024;

    public IdeaToPromptPage(IdeaToPromptViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is IdeaToPromptViewModel viewModel)
            viewModel.PrepareForNavigation();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch
        {
            // Back is best-effort; a failed pop just leaves the user on the page.
        }
    }

    // Segmented Text | Image source toggle. The VM's SourceMode is a settable observable
    // property, so the buttons just assign it; the active-segment look is driven by
    // IsTextSource/IsImageSource DataTriggers in XAML.
    private void OnSelectTextSource(object? sender, EventArgs e)
    {
        if (BindingContext is IdeaToPromptViewModel { IsBusy: false } vm)
            vm.SourceMode = IdeaSourceMode.Text;
    }

    private void OnSelectImageSource(object? sender, EventArgs e)
    {
        if (BindingContext is IdeaToPromptViewModel { IsBusy: false } vm)
            vm.SourceMode = IdeaSourceMode.Image;
    }

    private void OnWorkspaceSizeChanged(object? sender, EventArgs e)
    {
        if (WorkspaceGrid.Width <= 0) return;

        var mode = WorkspaceGrid.Width >= 760 ? WorkspaceLayoutMode.Wide : WorkspaceLayoutMode.Narrow;
        if (mode == _workspaceLayoutMode) return;

        _workspaceLayoutMode = mode;
        if (mode == WorkspaceLayoutMode.Wide)
        {
            SetColumns(new GridLength(5, GridUnitType.Star), new GridLength(4, GridUnitType.Star));
            SetRows(new GridLength(1, GridUnitType.Star));
            WorkspaceGrid.ColumnSpacing = 16;
            WorkspaceGrid.RowSpacing = 16;
            PlacePane(SourcePane, row: 0, column: 0);
            PlacePane(ResultPane, row: 0, column: 1);
        }
        else
        {
            SetColumns(new GridLength(1, GridUnitType.Star));
            SetRows(new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star));
            WorkspaceGrid.ColumnSpacing = 0;
            WorkspaceGrid.RowSpacing = 16;
            PlacePane(SourcePane, row: 0, column: 0);
            PlacePane(ResultPane, row: 1, column: 0);
        }
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

    private static void PlacePane(BindableObject pane, int row, int column)
    {
        Grid.SetRow(pane, row);
        Grid.SetColumn(pane, column);
        Grid.SetColumnSpan(pane, 1);
    }

    private void OnReferenceImageDragOver(object? sender, DragEventArgs e)
    {
        var winArgs = e.PlatformArgs?.DragEventArgs;
        if (winArgs?.DataView is { } dv && ContainsReferenceImageDropData(dv))
        {
            e.AcceptedOperation = MauiDataPackageOperation.Copy;
            winArgs.AcceptedOperation = WinDataPackageOperation.Copy;
            winArgs.DragUIOverride.Caption = "Use as vision reference image";
        }
    }

    private async void OnReferenceImageDropped(object? sender, DropEventArgs e)
    {
        if (BindingContext is not IdeaToPromptViewModel viewModel)
            return;

        var dv = e.PlatformArgs?.DragEventArgs?.DataView;
        if (dv is null || !ContainsReferenceImageDropData(dv)) return;

        try
        {
            var storageImportAttempted = false;
            if (dv.Contains(StandardDataFormats.StorageItems))
            {
                var items = await dv.GetStorageItemsAsync();
                var files = items.OfType<StorageFile>().ToList();
                var imageFiles = files.Where(IsImageStorageFile).ToList();
                var skipped = items.Count - imageFiles.Count;
                storageImportAttempted = items.Count > 0;
                var imported = false;

                if (imageFiles.Count > 0)
                {
                    imported = await TryUseStorageFileAsync(viewModel, imageFiles[0]);
                }

                if (imported && (skipped > 0 || imageFiles.Count > 1))
                {
                    var skippedCount = skipped + Math.Max(0, imageFiles.Count - 1);
                    viewModel.StatusMessage = $"Using 1 reference image; skipped {skippedCount} other file(s).";
                    viewModel.StatusKind = StatusKind.Warning;
                }

                if (imported)
                    return;
            }

            var url = await TryGetReferenceImageUrlAsync(dv);
            if (url is not null && await viewModel.SetReferenceImageFromUrlAsync(url))
                return;

            if (dv.Contains(StandardDataFormats.Bitmap))
            {
                var bitmapBytes = await TryReadBitmapAsync(dv);
                if (bitmapBytes is { Length: > 0 })
                {
                    viewModel.SetReferenceImageFromBytes("browser-reference.png", bitmapBytes, sourcePath: "browser bitmap");
                    return;
                }
            }

            if (url is null)
            {
                viewModel.StatusMessage = storageImportAttempted
                    ? "Couldn't read that dropped item as an image. Try dropping the image file itself or an http/https image URL."
                    : "Drop an image file, an image URL, or browser image data.";
                viewModel.StatusKind = StatusKind.Error;
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Error using reference image: {ex.Message}";
            viewModel.StatusKind = StatusKind.Error;
        }
    }

    private async void OnPasteReferenceImageClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not IdeaToPromptViewModel { IsBusy: false } viewModel)
            return;

        try
        {
            var clipboardData = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (clipboardData is null || !ContainsReferenceImageDropData(clipboardData))
            {
                viewModel.StatusMessage = "The clipboard doesn't contain an image, image file, or image URL.";
                viewModel.StatusKind = StatusKind.Warning;
                return;
            }

            if (await TryImportReferenceImageAsync(viewModel, clipboardData, "pasted"))
                return;

            viewModel.StatusMessage = "Couldn't read the clipboard image. Try copying the image itself or save it and use Pick image.";
            viewModel.StatusKind = StatusKind.Error;
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"Error pasting reference image: {ex.Message}";
            viewModel.StatusKind = StatusKind.Error;
        }
    }

    private static async Task<bool> TryImportReferenceImageAsync(
        IdeaToPromptViewModel viewModel,
        WinDataPackageView dataView,
        string source)
    {
        if (dataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await dataView.GetStorageItemsAsync();
            var imageFile = items.OfType<StorageFile>().FirstOrDefault(IsImageStorageFile);
            if (imageFile is not null && await TryUseStorageFileAsync(viewModel, imageFile))
                return true;
        }

        if (dataView.Contains(StandardDataFormats.Bitmap))
        {
            var bitmapBytes = await TryReadBitmapAsync(dataView);
            if (bitmapBytes is { Length: > 0 })
                return viewModel.SetReferenceImageFromBytes($"{source}-reference.png", bitmapBytes, $"{source} bitmap");
        }

        // Browser clipboards commonly expose both a bitmap and the source URL. Prefer the bitmap
        // already present on the clipboard so Paste stays local and does not wait on a network
        // download. The URL remains a fallback for clipboards that do not provide usable pixels.
        var url = await TryGetReferenceImageUrlAsync(dataView);
        if (url is not null && await viewModel.SetReferenceImageFromUrlAsync(url))
            return true;

        return false;
    }

    private static bool ContainsReferenceImageDropData(WinDataPackageView dataView) =>
        dataView.Contains(StandardDataFormats.StorageItems)
        || dataView.Contains(StandardDataFormats.WebLink)
        || dataView.Contains(StandardDataFormats.Text)
        || dataView.Contains(StandardDataFormats.Html)
        || dataView.Contains(StandardDataFormats.Bitmap);

    private static bool IsImageStorageFile(StorageFile file) =>
        ImageExtensions.Contains(file.FileType)
        || ImageExtensions.Contains(Path.GetExtension(file.Path))
        || file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> TryUseStorageFileAsync(
        IdeaToPromptViewModel viewModel,
        StorageFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
        {
            await viewModel.SetReferenceImageFromPathAsync(file.Path);
            return viewModel.HasReferenceImage;
        }

        var imageBytes = await TryReadStorageFileAsync(file);
        if (imageBytes is not { Length: > 0 })
        {
            viewModel.StatusMessage = "Couldn't read that dropped image. Try saving it locally first, then drop the file.";
            viewModel.StatusKind = StatusKind.Error;
            return false;
        }

        var fileName = string.IsNullOrWhiteSpace(file.Name)
            ? "dropped-reference-image"
            : file.Name;
        var sourcePath = string.IsNullOrWhiteSpace(file.Path)
            ? "dropped storage file"
            : file.Path;
        return viewModel.SetReferenceImageFromBytes(fileName, imageBytes, sourcePath);
    }

    private static async Task<byte[]?> TryReadStorageFileAsync(StorageFile file)
    {
        using var stream = await file.OpenReadAsync();
        return await TryReadRandomAccessStreamAsync(stream);
    }

    private static async Task<string?> TryGetReferenceImageUrlAsync(WinDataPackageView dataView)
    {
        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            var uri = await dataView.GetWebLinkAsync();
            if (IsHttpOrHttps(uri))
                return uri.ToString();
        }

        if (dataView.Contains(StandardDataFormats.Text))
        {
            var text = await dataView.GetTextAsync();
            if (TryExtractHttpUrl(text, out var url))
                return url;
        }

        if (dataView.Contains(StandardDataFormats.Html))
        {
            var html = await dataView.GetHtmlFormatAsync();
            var fragment = HtmlFormatHelper.GetStaticFragment(html);
            if (TryExtractImageSrc(fragment, out var imageUrl)
                || TryExtractHttpUrl(fragment, out imageUrl))
                return imageUrl;
        }

        return null;
    }

    private static async Task<byte[]?> TryReadBitmapAsync(WinDataPackageView dataView)
    {
        var reference = await dataView.GetBitmapAsync();
        if (reference is null)
            return null;

        using var stream = await reference.OpenReadAsync();
        return await TryReadRandomAccessStreamAsync(stream);
    }

    private static async Task<byte[]?> TryReadRandomAccessStreamAsync(IRandomAccessStream stream)
    {
        if (stream.Size == 0 || stream.Size > MaxReferenceImageBytes || stream.Size > int.MaxValue)
            return null;

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var requested = (uint)stream.Size;
        var loaded = await reader.LoadAsync(requested);
        if (loaded == 0)
            return null;

        var bytes = new byte[checked((int)loaded)];
        reader.ReadBytes(bytes);
        return bytes;
    }

    internal static bool TryExtractImageSrc(string? html, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(html)) return false;

        var match = Regex.Match(
            html,
            "<img\\b[^>]*\\bsrc\\s*=\\s*(?:\"(?<url>[^\"]+)\"|'(?<url>[^']+)'|(?<url>[^\\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;

        var candidate = System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsHttpOrHttps(uri))
            return false;

        url = uri.ToString();
        return true;
    }

    internal static bool TryExtractHttpUrl(string? text, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = Regex.Match(
            text,
            "https?://[^\\s\"'<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;

        var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IsHttpOrHttps(uri))
            return false;

        url = uri.ToString();
        return true;
    }

    private static bool IsHttpOrHttps(Uri? uri) =>
        uri is not null && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
