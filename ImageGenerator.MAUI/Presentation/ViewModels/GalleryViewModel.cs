using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using static ImageGenerator.MAUI.Presentation.Common.UiDispatcher;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GalleryViewModel : ObservableObject, IDisposable
{
    // Coalesce bursts of FileSystemWatcher events. A single image save fires Created + Changed +
    // a stat-touch from the in-flight guard's stamp; debouncing collapses them to one Refresh.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly IGalleryService _galleryService;
    private readonly IFileLauncher _fileLauncher;
    private readonly ICivitaiPostingService _civitaiPostingService;
    private readonly IUiStateStore _uiStateStore;
    private readonly ILogger<GalleryViewModel> _logger;
    private readonly string _watchDirectory;

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private readonly object _debounceGate = new();

    public ObservableCollection<GalleryItem> Items { get; } = [];

    // Bound to the CollectionView's SelectedItems (SelectionMode="Multiple"). Object-typed
    // because that is the contract of CollectionView.SelectedItems.
    public ObservableCollection<object> SelectedItems { get; } = [];

    public bool HasSelection => SelectedItems.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isBusy;

    public bool IsEmpty => Items.Count == 0 && !IsBusy;

    // --- CivitAI batch posting (selected images → ONE draft post) ---

    // The CivitAI model reference (URL or version id) the draft should land in. Shared with the
    // main page via the same persisted UiStateStore key, so the target only has to be set once.
    [ObservableProperty]
    private string _civitaiModelRef = string.Empty;

    // Attach each image's embedded generation data (prompt/seed/model) to the post. Defaults ON
    // for the Gallery: posting here is deliberate curation, and the data is already in the files.
    [ObservableProperty]
    private bool _civitaiIncludeMeta = true;

    [ObservableProperty]
    private string? _civitaiStatusMessage;

    // The created draft's URL — surfaces the "Open draft" button so the user can review + publish.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastPost))]
    private string? _lastPostUrl;

    public bool HasLastPost => !string.IsNullOrEmpty(LastPostUrl);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PostSelectedAsOnePostCommand))]
    private bool _isPosting;

    private bool CanPostSelected => HasSelection && !IsPosting;

    partial void OnCivitaiModelRefChanged(string value) => _uiStateStore.PersistCivitaiModelRef(value ?? string.Empty);

    // Sort modes the user can pick. Display strings double as the SelectedSortMode value —
    // simpler than an enum + IValueConverter when the UI already uses a Picker bound to a
    // string list. Default matches the original filename-desc behaviour ("yyyyMMdd_HHmmss"
    // prefix == newest first).
    public const string SortNewestFirst = "Newest first";
    public const string SortOldestFirst = "Oldest first";
    public const string SortNameAscending = "Name (A→Z)";
    public const string SortNameDescending = "Name (Z→A)";
    public const string SortLargestFirst = "Largest first";
    public const string SortSmallestFirst = "Smallest first";

    public IReadOnlyList<string> SortModes { get; } = new[]
    {
        SortNewestFirst, SortOldestFirst,
        SortNameAscending, SortNameDescending,
        SortLargestFirst, SortSmallestFirst,
    };

    [ObservableProperty]
    private string _selectedSortMode = SortNewestFirst;

    // Keep an unordered snapshot of the last enumeration so a sort-mode change can re-sort
    // without paying the I/O cost of re-walking the directory.
    private List<GalleryItem> _lastSnapshot = [];

    public GalleryViewModel(
        IGalleryService galleryService,
        IFileLauncher fileLauncher,
        ICivitaiPostingService civitaiPostingService,
        IUiStateStore uiStateStore,
        ILogger<GalleryViewModel> logger)
    {
        _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
        _fileLauncher = fileLauncher ?? throw new ArgumentNullException(nameof(fileLauncher));
        _civitaiPostingService = civitaiPostingService ?? throw new ArgumentNullException(nameof(civitaiPostingService));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _watchDirectory = OutputPaths.GeneratedImagesDirectory;

        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        // SelectedItems is mutated by the CollectionView (multi-select); recompute the gating
        // flag and the post command's CanExecute whenever the selection changes.
        SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            PostSelectedAsOnePostCommand.NotifyCanExecuteChanged();
        };
    }

    public async Task OnAppearingAsync()
    {
        try
        {
            // Restore the shared CivitAI model target (same key the main page persists).
            var savedModelRef = _uiStateStore.LoadCivitaiModelRef();
            if (!string.IsNullOrEmpty(savedModelRef)) CivitaiModelRef = savedModelRef;

            StartWatcher();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryVM.{Op}", "OnAppearing");
        }
    }

    public void OnDisappearing()
    {
        try
        {
            StopWatcher();
            CancelDebounce();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryVM.{Op}", "OnDisappearing");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        DispatchToUi(() => IsBusy = true);
        try
        {
            var snapshot = new List<GalleryItem>();
            await foreach (var item in _galleryService.EnumerateAsync(ct))
            {
                snapshot.Add(item);
            }

            _lastSnapshot = snapshot;
            ApplySnapshotToItems();
        }
        catch (OperationCanceledException)
        {
            // Debounce window canceled the in-flight refresh; the next one will replace it.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryVM.{Op}", "Refresh");
        }
        finally
        {
            DispatchToUi(() => IsBusy = false);
        }
    }

    partial void OnSelectedSortModeChanged(string value)
    {
        // Re-sort the cached snapshot rather than re-walking the disk. Lighter-weight than
        // a full Refresh — and a Refresh is only one click away if the user wants both.
        ApplySnapshotToItems();
    }

    private void ApplySnapshotToItems()
    {
        var sorted = SortSnapshot(_lastSnapshot, SelectedSortMode);
        DispatchToUi(() =>
        {
            Items.Clear();
            foreach (var item in sorted) Items.Add(item);
        });
    }

    // Date-based sorts use CreationTime (captured into GalleryItem.CreatedAt by the service),
    // not the filename's leading timestamp prefix. Filename sorting only worked for files we
    // saved ourselves; externally-added images (drag-drops, downloads, screenshots) wouldn't
    // have the yyyyMMdd_HHmmss_ prefix and would land in arbitrary positions.
    private static IEnumerable<GalleryItem> SortSnapshot(IEnumerable<GalleryItem> snapshot, string mode) => mode switch
    {
        SortOldestFirst    => snapshot.OrderBy(i => i.CreatedAt),
        SortNameAscending  => snapshot.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase),
        SortNameDescending => snapshot.OrderByDescending(i => i.FileName, StringComparer.OrdinalIgnoreCase),
        SortLargestFirst   => snapshot.OrderByDescending(i => i.FileSize),
        SortSmallestFirst  => snapshot.OrderBy(i => i.FileSize),
        _                  => snapshot.OrderByDescending(i => i.CreatedAt), // SortNewestFirst (default)
    };

    [RelayCommand]
    private void OpenInViewer(GalleryItem? item)
    {
        if (item is null) return;
        try
        {
            _fileLauncher.Launch(item.FilePath);
        }
        catch (Exception ex)
        {
            // Process.Start can throw if the file was deleted between watcher tick and click,
            // or if no shell association exists. Log so we can see a pattern, but don't
            // surface to the user — the missing tile will disappear on the next refresh.
            _logger.LogError(ex, "GalleryVM.{Op}", "OpenInViewer");
        }
    }

    /// <summary>
    /// Uploads every selected image into ONE CivitAI draft post (a single gallery card). Drafts —
    /// never publishes — so the user reviews and publishes on the site. The warning/confirmation
    /// is shown by the page before this runs; the command itself just does the work.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPostSelected))]
    private async Task PostSelectedAsOnePostAsync(CancellationToken ct = default)
    {
        // Snapshot now: the selection can change while uploads are in flight.
        var selected = SelectedItems.OfType<GalleryItem>().ToList();
        if (selected.Count == 0) return;

        DispatchToUi(() =>
        {
            IsPosting = true;
            LastPostUrl = null;
            CivitaiStatusMessage = $"Posting {selected.Count} image(s) to CivitAI…";
        });

        try
        {
            var images = new List<CivitaiImagePost>(selected.Count);
            string? firstPrompt = null;
            foreach (var item in selected)
            {
                var fileMeta = await _galleryService.ReadMetadataAsync(item.FilePath, ct);
                firstPrompt ??= fileMeta is not null && fileMeta.TryGetValue("Prompt", out var p) ? p : null;
                var meta = CivitaiIncludeMeta ? CivitaiMetaBuilder.BuildFromFileMetadata(fileMeta) : null;
                images.Add(new CivitaiImagePost(item.FilePath, meta));
            }

            var modelVersionId = CivitaiModelReference.ParseVersionId(CivitaiModelRef);
            // Non-empty text that parses to nothing is a typo, not a "no model" choice — say so
            // instead of silently posting to the profile.
            var refNotRecognized = !string.IsNullOrWhiteSpace(CivitaiModelRef) && modelVersionId is null;

            var title = CivitaiTitleBuilder.Build(firstPrompt ?? string.Empty);
            var result = await _civitaiPostingService.PostImagesAsync(
                images, title, modelVersionId, publish: false, ct);

            _logger.LogInformation(
                "Gallery CivitAI draft finished Success={Success} PostId={PostId} Images={Count} ModelVersionId={ModelVersionId}",
                result.Success, result.PostId, images.Count, modelVersionId);

            DispatchToUi(() =>
            {
                LastPostUrl = result.PostUrl;
                CivitaiStatusMessage = result.Success
                    ? refNotRecognized
                        ? $"{result.Message} Model reference not recognized — drafted without a model link."
                        : result.Message
                    : $"CivitAI draft failed: {result.Message}";
            });
        }
        catch (OperationCanceledException)
        {
            DispatchToUi(() => CivitaiStatusMessage = "CivitAI posting canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gallery CivitAI posting threw");
            DispatchToUi(() => CivitaiStatusMessage = $"CivitAI draft failed: {ex.Message}");
        }
        finally
        {
            DispatchToUi(() => IsPosting = false);
        }
    }

    [RelayCommand]
    private void OpenLastPost()
    {
        if (string.IsNullOrEmpty(LastPostUrl)) return;
        try
        {
            _fileLauncher.Launch(LastPostUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryVM.{Op}", "OpenLastPost");
        }
    }

    [RelayCommand]
    private async Task OpenOutputFolderAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_watchDirectory);
                _fileLauncher.Launch(_watchDirectory);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryVM.{Op}", "OpenOutputFolder");
        }
    }

    private void StartWatcher()
    {
        if (_watcher is not null) return;
        try
        {
            if (!Directory.Exists(_watchDirectory)) return;

            var watcher = new FileSystemWatcher(_watchDirectory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Filters.Add("*.png");
            watcher.Filters.Add("*.jpg");
            watcher.Filters.Add("*.jpeg");
            watcher.Filters.Add("*.webp");
            watcher.Created += OnWatcherEvent;
            watcher.Changed += OnWatcherEvent;
            watcher.Deleted += OnWatcherEvent;
            watcher.Renamed += OnWatcherEvent;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch
        {
            // Insufficient perms, deleted dir, AV-blocked. Manual Refresh still works.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void StopWatcher()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is null) return;
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnWatcherEvent;
            watcher.Changed -= OnWatcherEvent;
            watcher.Deleted -= OnWatcherEvent;
            watcher.Renamed -= OnWatcherEvent;
            watcher.Error -= OnWatcherError;
        }
        catch
        {
            // Disposed watchers can throw on event detach. Ignore.
        }
        watcher.Dispose();
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        try { TriggerDebouncedRefresh(); }
        catch (Exception ex) { _logger.LogError(ex, "GalleryVM.{Op}", "OnWatcherEvent"); }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // The watcher itself raised an Error event (buffer overflow, access denied). Refresh
        // anyway so the UI reflects current state instead of stalling on stale data.
        try { TriggerDebouncedRefresh(); }
        catch (Exception ex) { _logger.LogError(ex, "GalleryVM.{Op}", "OnWatcherError"); }
    }

    private void TriggerDebouncedRefresh()
    {
        CancellationTokenSource cts;
        lock (_debounceGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }

        // Fire-and-forget Task: any escaping exception would otherwise only surface via
        // TaskScheduler.UnobservedTaskException after a GC, and on a fast-fail crash the GC
        // doesn't run. Catch-and-log here is the only point we can guarantee disk persistence.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceWindow, cts.Token);
                if (cts.IsCancellationRequested) return;
                await RefreshAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when a newer event supersedes this debounce window.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GalleryVM.{Op}", "DebouncedRefresh");
            }
        });
    }

    private void CancelDebounce()
    {
        lock (_debounceGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }

    public void Dispose()
    {
        StopWatcher();
        CancelDebounce();
        GC.SuppressFinalize(this);
    }
}
