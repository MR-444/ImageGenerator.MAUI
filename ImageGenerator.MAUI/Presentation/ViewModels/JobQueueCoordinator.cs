using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using static ImageGenerator.MAUI.Presentation.Common.UiDispatcher;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// The job cards and what runs them: the queue, its summaries, one run per card (GPU gate,
/// progress, outcome, the optional CivitAI post) and the ComfyUI VRAM free afterwards.
/// Created and held by GeneratorViewModel; MainPage binds it as Queue.*.
/// </summary>
public sealed partial class JobQueueCoordinator : ObservableObject
{
    private readonly IJobRunner _jobRunner;
    private readonly ICivitaiPostingService _civitaiPostingService;
    private readonly IGalleryService _galleryService;
    private readonly IComfyUiVramService? _comfyVram;
    private readonly IGpuGate? _gpuGate;
    private readonly AppSettingsCoordinator _settings;
    private readonly Func<bool> _isBatchRunning;
    private readonly Action<string> _flash;
    private readonly ILogger _logger;

    // A batch holds one GPU lease across all its ComfyUI jobs; CompleteBatchAsync releases it.
    private IDisposable? _batchGpuLease;
    private string? _batchComfyModel;

    public JobQueueCoordinator(
        IJobRunner jobRunner,
        ICivitaiPostingService civitaiPostingService,
        IGalleryService galleryService,
        IComfyUiVramService? comfyVram,
        IGpuGate? gpuGate,
        AppSettingsCoordinator settings,
        Func<bool> isBatchRunning,
        Action<string> flash,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(jobRunner);
        ArgumentNullException.ThrowIfNull(civitaiPostingService);
        ArgumentNullException.ThrowIfNull(galleryService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isBatchRunning);
        ArgumentNullException.ThrowIfNull(flash);
        ArgumentNullException.ThrowIfNull(logger);
        _jobRunner = jobRunner;
        _civitaiPostingService = civitaiPostingService;
        _galleryService = galleryService;
        _comfyVram = comfyVram;
        _gpuGate = gpuGate;
        _settings = settings;
        _isBatchRunning = isBatchRunning;
        _flash = flash;
        _logger = logger;

        Jobs.CollectionChanged += OnJobsCollectionChanged;
    }

    public ObservableCollection<GenerationJob> Jobs { get; } = [];
    public bool HasJobs => Jobs.Count > 0;
    public bool HasFinishedJobs => Jobs.Any(j => j.IsFinished);

    // "2 running · 1 queued · 5 done". A queued batch job is not running and still at StatusKind.Info.
    public string JobStatusSummary
    {
        get
        {
            var running = Jobs.Count(j => j.IsRunning);
            var queued = Jobs.Count(j => j is { IsRunning: false, StatusKind: StatusKind.Info });
            var done = Jobs.Count(j => j.IsFinished);
            var parts = new List<string>(3);
            if (running > 0) parts.Add($"{running} running");
            if (queued > 0) parts.Add($"{queued} queued");
            if (done > 0) parts.Add($"{done} done");
            return parts.Count > 0 ? string.Join(" · ", parts) : "No jobs yet";
        }
    }

    // The newest saved job: the one featured card. Canceled and failed jobs never become it.
    [ObservableProperty]
    private GenerationJob? _latestCompletedJob;

    // Images in the output folder; null until LoadTotalImagesGeneratedAsync has counted them.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalImagesGeneratedText))]
    private int? _totalImagesGenerated;

    public string TotalImagesGeneratedText =>
        TotalImagesGenerated is { } n ? $"{n} image{(n == 1 ? "" : "s")}" : "…";

    /// <summary>Puts a card at the top of the queue, on the UI thread.</summary>
    public void Enqueue(GenerationJob job) => DispatchToUi(() => Jobs.Insert(0, job));

    // Per-job PropertyChanged is attached here so a job turning finished re-evaluates the gates.
    private void OnJobsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => DispatchToUi(() =>
        {
            if (e.OldItems is not null)
                foreach (GenerationJob job in e.OldItems) job.PropertyChanged -= OnJobPropertyChanged;
            if (e.NewItems is not null)
                foreach (GenerationJob job in e.NewItems) job.PropertyChanged += OnJobPropertyChanged;

            OnPropertyChanged(nameof(HasJobs));
            RaiseFinishedJobsChanged();
        });

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenerationJob.IsFinished))
            DispatchToUi(RaiseFinishedJobsChanged);
    }

    private void RaiseFinishedJobsChanged()
    {
        OnPropertyChanged(nameof(HasFinishedJobs));
        OnPropertyChanged(nameof(JobStatusSummary));
        ClearFinishedJobsCommand.NotifyCanExecuteChanged();
    }

    // Drops finished cards; running and queued jobs stay. The featured card is always finished.
    [RelayCommand(CanExecute = nameof(HasFinishedJobs))]
    private void ClearFinishedJobs()
    {
        var finished = Jobs.Where(j => j.IsFinished).ToList();
        foreach (var job in finished) Jobs.Remove(job);

        if (LatestCompletedJob is not null && !Jobs.Contains(LatestCompletedJob))
            LatestCompletedJob = null;

        _flash($"Cleared {finished.Count} finished job{(finished.Count == 1 ? "" : "s")}.");
    }

    public Task RunAsync(GenerationJob job) => RunJobAsync(job, isBatchJob: false);

    public Task RunBatchJobAsync(GenerationJob job) => RunJobAsync(job, isBatchJob: true);

    private async Task RunJobAsync(GenerationJob job, bool isBatchJob)
    {
        // A ComfyUI render and local Ollama work share one GPU's VRAM. Hold the gate from before
        // submit until after the VRAM free. Other providers and split-host setups skip it.
        var isComfyJob = ModelConstants.ComfyUi.IsId(job.Parameters.Model);
        var gpuGated = _gpuGate is not null
            && isComfyJob
            && GpuColocation.SameHost(_settings.ComfyUiBaseUrl, _settings.OllamaBaseUrl);

        IDisposable? singleJobLease = null;
        if (gpuGated)
        {
            try
            {
                if (isBatchJob)
                {
                    _batchGpuLease ??= await _gpuGate!.AcquireAsync(job.Cts.Token);
                    _batchComfyModel = job.Parameters.Model;
                }
                else
                {
                    singleJobLease = await _gpuGate!.AcquireAsync(job.Cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Canceled while queued behind another GPU workload; nothing rendered.
                MarkCanceled(job);
                DispatchToUi(() =>
                {
                    job.IsRunning = false;
                    job.HasProgress = false;
                });
                job.Cts.Dispose();
                return;
            }
        }
        else if (isBatchJob && isComfyJob)
        {
            // Remember the batch's model so CompleteBatchAsync frees VRAM even if the
            // picker changes mid-batch.
            _batchComfyModel = job.Parameters.Model;
        }

        try
        {
            _logger.LogInformation(
                "Job started Model={Model} Seed={Seed}", job.Parameters.Model, job.Parameters.Seed);

            // ComfyUI sampler progress. A report arriving after the outcome must not overwrite it.
            var progress = new Progress<JobProgress>(p => DispatchToUi(() =>
            {
                if (!job.IsRunning) return;
                job.StatusMessage = p.Message;
                if (p.Percent is { } pct)
                {
                    job.Progress = pct;
                    job.HasProgress = true;
                }
            }));

            var outcome = await _jobRunner.RunAsync(job.Parameters, job.Cts.Token, progress);

            _logger.LogInformation(
                "Job finished Kind={Kind} Path={Path}", outcome.Kind, outcome.SavedPath);

            // MAUI drops PropertyChanged raised off the UI thread.
            DispatchToUi(() =>
            {
                job.ResultPath = outcome.SavedPath;
                if (outcome.SavedPath is not null)
                {
                    // Exactly one card is featured: the newest saved one.
                    if (LatestCompletedJob is not null) LatestCompletedJob.IsFeatured = false;
                    job.IsFeatured = true;
                    LatestCompletedJob = job;

                    // Before the startup count finishes there is nothing to increment.
                    if (TotalImagesGenerated is { } n) TotalImagesGenerated = n + 1;
                }
                job.StatusMessage = outcome.Message;
                job.StatusKind = outcome.Kind switch
                {
                    JobOutcomeKind.Saved => StatusKind.Success,
                    // The runner reports a cancel during polling as Failed with a "canceled" message.
                    _ when outcome.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                        => StatusKind.Canceled,
                    _ => StatusKind.Error
                };
            });

            // Fire-and-forget: a slow upload must not delay the next batch job, and its failure
            // never changes the job outcome.
            if (outcome is { Kind: JobOutcomeKind.Saved, SavedPath: not null }
                && job.Parameters.PostToCivitai)
            {
                _ = PostJobToCivitaiAsync(job, outcome.SavedPath);
            }
        }
        catch (OperationCanceledException)
        {
            MarkCanceled(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job finished Kind=Threw Model={Model}", job.Parameters.Model);
            var msg = $"Error: {ex.Message}";
            DispatchToUi(() =>
            {
                job.StatusMessage = msg;
                job.StatusKind = StatusKind.Error;
            });
        }
        finally
        {
            DispatchToUi(() =>
            {
                job.IsRunning = false;
                job.HasProgress = false;
            });
            job.Cts.Dispose();
        }

        // Batch jobs keep the lease until CompleteBatchAsync runs at batch end.
        if (isBatchJob) return;

        // Free VRAM before releasing the gate, so queued GPU work gets a freed card.
        try
        {
            await MaybeFreeComfyUiVramAsync(job.Parameters.Model);
        }
        finally
        {
            singleJobLease?.Dispose();
        }
    }

    private void MarkCanceled(GenerationJob job)
    {
        _logger.LogInformation("Job finished Kind=Canceled Path=(null)");
        DispatchToUi(() =>
        {
            job.StatusMessage = "Canceled.";
            job.StatusKind = StatusKind.Canceled;
        });
    }

    /// <summary>
    /// Runs once when a batch ends: frees ComfyUI VRAM once for the whole batch (a per-job free
    /// reloads the checkpoint every time), then releases the batch's GPU lease.
    /// </summary>
    public async Task CompleteBatchAsync()
    {
        var lease = _batchGpuLease;
        var model = _batchComfyModel;
        _batchGpuLease = null;
        _batchComfyModel = null;

        if (lease is null && model is null) return;

        try
        {
            if (model is not null)
                await MaybeFreeComfyUiVramAsync(model);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <summary>
    /// Frees ComfyUI's GPU memory if "Free GPU memory after rendering" is on. No-op for other
    /// models, during a batch, or without a VRAM service. The service swallows failures.
    /// </summary>
    internal async Task MaybeFreeComfyUiVramAsync(string model)
    {
        if (!_settings.FreeVramAfterRendering
            || _comfyVram is null
            || _isBatchRunning()
            || !ModelConstants.ComfyUi.IsId(model))
        {
            return;
        }

        await _comfyVram.TryFreeAsync();
    }

    internal async Task PostJobToCivitaiAsync(GenerationJob job, string savedPath)
    {
        DispatchToUi(() => job.CivitaiStatusMessage = "Posting to CivitAI…");
        try
        {
            var meta = job.Parameters.CivitaiIncludeMeta
                ? CivitaiMetaBuilder.Build(job.Parameters)
                : null;
            var modelVersionId = CivitaiModelReference.ParseVersionId(job.Parameters.CivitaiModelRef);
            // Text that parses to nothing is a typo, not a "no model" choice; say so.
            var refNotRecognized =
                !string.IsNullOrWhiteSpace(job.Parameters.CivitaiModelRef) && modelVersionId is null;

            var result = await _civitaiPostingService.PostImageAsync(
                savedPath, CivitaiTitleBuilder.Build(job.Prompt), meta, modelVersionId);

            _logger.LogInformation(
                "CivitAI post finished Success={Success} PostId={PostId} ModelVersionId={ModelVersionId}",
                result.Success, result.PostId, modelVersionId);
            DispatchToUi(() =>
            {
                job.CivitaiPostUrl = result.PostUrl;
                job.CivitaiStatusMessage = result.Success
                    ? refNotRecognized
                        ? $"{result.Message} Model reference not recognized — posted without model link."
                        : result.Message
                    : $"CivitAI post failed: {result.Message}";
            });
        }
        catch (Exception ex)
        {
            // The service returns failures as results; anything that still throws must not
            // escape a fire-and-forget continuation. The job stays Saved.
            _logger.LogWarning(ex, "CivitAI posting threw Path={Path}", savedPath);
            DispatchToUi(() => job.CivitaiStatusMessage = $"CivitAI post failed: {ex.Message}");
        }
    }

    // Called fire-and-forget from OnAppearing, so a large output folder never delays startup.
    public async Task LoadTotalImagesGeneratedAsync(CancellationToken ct = default)
    {
        try
        {
            var count = 0;
            await foreach (var _ in _galleryService.EnumerateAsync(ct)) count++;
            DispatchToUi(() => TotalImagesGenerated = count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Total-images count failed");
        }
    }
}
