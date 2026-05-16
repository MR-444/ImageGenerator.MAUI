using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GalleryViewModel : ObservableObject, IDisposable
{
    // Coalesce bursts of FileSystemWatcher events. A single image save fires Created + Changed +
    // a stat-touch from the in-flight guard's stamp; debouncing collapses them to one Refresh.
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly IGalleryService _galleryService;
    private readonly IFileLauncher _fileLauncher;
    private readonly ILogger<GalleryViewModel> _logger;
    private readonly string _watchDirectory;

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private readonly object _debounceGate = new();

    public ObservableCollection<GalleryItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isBusy;

    public bool IsEmpty => Items.Count == 0 && !IsBusy;

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
        ILogger<GalleryViewModel> logger)
    {
        _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
        _fileLauncher = fileLauncher ?? throw new ArgumentNullException(nameof(fileLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _watchDirectory = OutputPaths.GeneratedImagesDirectory;

        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task OnAppearingAsync()
    {
        try
        {
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

    private static void DispatchToUi(Action action)
    {
        try
        {
            if (MainThread.IsMainThread) action();
            else MainThread.BeginInvokeOnMainThread(action);
        }
        catch
        {
            // MainThread throws in unit-test contexts where WinRT isn't initialised;
            // running synchronously is safe because tests run on a single thread anyway.
            action();
        }
    }

    public void Dispose()
    {
        StopWatcher();
        CancelDebounce();
        GC.SuppressFinalize(this);
    }
}
