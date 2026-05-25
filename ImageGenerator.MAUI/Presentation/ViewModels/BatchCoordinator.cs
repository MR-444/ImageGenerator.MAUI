using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;
using static ImageGenerator.MAUI.Presentation.Common.UiDispatcher;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Sub-VM that owns the prompt-batch lifecycle: file-pick + parse, sequential job execution,
/// cancellation. Created and held by GeneratorViewModel; bound from the Run card on MainPage.
///
/// Cancellation contract: CancelBatch drains the queued jobs but lets the in-flight one
/// finish. The Replicate prediction is already paid for once submitted; killing mid-poll
/// would waste the image. The per-card Cancel button on a running job still aborts that
/// single job if the user really wants to.
/// </summary>
public sealed partial class BatchCoordinator : ObservableObject
{
    private readonly IPromptBatchParser _promptBatchParser;
    private readonly Func<ImageGenerationParameters> _parametersAccessor;
    private readonly Action<GenerationJob> _enqueueJob;
    private readonly Func<GenerationJob, Task> _runJob;
    private readonly Action<string, StatusKind> _setStatus;
    private readonly Func<string, Task> _addAsInputAsync;

    // Non-null only while a batch is actively running. CancelBatch flips it; RunBatchAsync
    // disposes and clears it in a finally so a second batch always starts with a fresh CTS.
    private CancellationTokenSource? _batchCts;

    [ObservableProperty]
    private bool _isBatchRunning;

    public BatchCoordinator(
        IPromptBatchParser promptBatchParser,
        Func<ImageGenerationParameters> parametersAccessor,
        Action<GenerationJob> enqueueJob,
        Func<GenerationJob, Task> runJob,
        Action<string, StatusKind> setStatus,
        Func<string, Task> addAsInputAsync)
    {
        _promptBatchParser = promptBatchParser ?? throw new ArgumentNullException(nameof(promptBatchParser));
        _parametersAccessor = parametersAccessor ?? throw new ArgumentNullException(nameof(parametersAccessor));
        _enqueueJob = enqueueJob ?? throw new ArgumentNullException(nameof(enqueueJob));
        _runJob = runJob ?? throw new ArgumentNullException(nameof(runJob));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _addAsInputAsync = addAsInputAsync ?? throw new ArgumentNullException(nameof(addAsInputAsync));
    }

    /// <summary>
    /// Opens a file picker for a .txt prompt file, parses it, and returns the prompts.
    /// Returns null on cancel, on empty parse, or on read/cap errors (status surface set
    /// in those cases). Public for the View to call from its button click handler.
    /// </summary>
    public async Task<IReadOnlyList<string>?> PickAndParsePromptsAsync()
    {
        var parameters = _parametersAccessor();

        // Pollinations models can run anonymously; only require the Replicate token when the
        // currently-selected model actually needs it.
        if (!ModelConstants.Pollinations.IsId(parameters.Model) && string.IsNullOrWhiteSpace(parameters.ApiToken))
        {
            _setStatus("Enter an API token before running a batch.", StatusKind.Error);
            return null;
        }

        _setStatus("Opening file picker…", StatusKind.Info);

        FileResult? result;
        try
        {
            result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Pick a prompt textfile",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt" } },
                }),
            });
        }
        catch (Exception ex)
        {
            _setStatus($"Couldn't open file picker: {ex.Message}", StatusKind.Error);
            return null;
        }

        if (result == null)
        {
            _setStatus(string.Empty, StatusKind.None);
            return null;
        }

        try
        {
            var text = await File.ReadAllTextAsync(result.FullPath);
            var prompts = _promptBatchParser.Parse(text);
            if (prompts.Count == 0)
            {
                _setStatus("No prompts found in file.", StatusKind.Warning);
                return null;
            }
            _setStatus($"Loaded {prompts.Count} prompts from {result.FileName}.", StatusKind.Info);
            return prompts;
        }
        catch (PromptBatchTooLargeException ex)
        {
            _setStatus($"File contains {ex.PromptCount} prompts; cap is {ex.MaxAllowed}.", StatusKind.Error);
            return null;
        }
        catch (Exception ex)
        {
            _setStatus($"Couldn't read prompt file: {ex.Message}", StatusKind.Error);
            return null;
        }
    }

    /// <summary>
    /// Runs the supplied prompts as a sequential batch using the currently-selected model
    /// and parameters. Each prompt becomes its own <see cref="GenerationJob"/> queued at the
    /// top of the host's Jobs collection in original file order. Failures don't abort the batch.
    /// </summary>
    public async Task RunBatchAsync(IReadOnlyList<string> prompts)
    {
        if (prompts is null || prompts.Count == 0) return;
        if (IsBatchRunning) return; // re-entrancy guard

        _batchCts = new CancellationTokenSource();
        IsBatchRunning = true;

        try
        {
            var parameters = _parametersAccessor();

            // Build all jobs as Queued, freezing per-prompt parameter snapshots up-front
            // so a model swap mid-batch can't bleed into pending jobs.
            var batch = new List<GenerationJob>(prompts.Count);
            for (var i = 0; i < prompts.Count; i++)
            {
                if (parameters.RandomizeSeed)
                    parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);

                var snapshot = parameters.Clone();
                snapshot.Prompt = prompts[i];

                var job = new GenerationJob(snapshot, _addAsInputAsync)
                {
                    IsRunning = false,
                    StatusKind = StatusKind.Info,
                    StatusMessage = $"Queued ({i + 1}/{prompts.Count})"
                };
                batch.Add(job);
            }

            // Insert reverse so the FIRST prompt sits at the top of the Jobs list, matching
            // single-prompt mode's "newest first via Insert(0, …)" convention. The host
            // enqueueJob callback wraps the Jobs.Insert in DispatchToUi.
            for (var i = batch.Count - 1; i >= 0; i--)
            {
                _enqueueJob(batch[i]);
            }

            var succeeded = 0;
            var failed = 0;
            var canceled = 0;

            foreach (var job in batch)
            {
                if (_batchCts.IsCancellationRequested)
                {
                    // Job-state mutations fire PropertyChanged; HttpClient continuations from the
                    // prior iteration can leave us on a ThreadPool thread, so marshal back.
                    DispatchToUi(() =>
                    {
                        job.IsRunning = false;
                        job.StatusKind = StatusKind.Canceled;
                        job.StatusMessage = "Canceled.";
                    });
                    canceled++;
                    continue;
                }

                // CancelBatch drains the queue but lets the in-flight job finish — the
                // Replicate prediction is already paid for and the image would be wasted
                // if we killed mid-poll. The per-card Cancel button still aborts a single
                // running job if the user really wants to.
                DispatchToUi(() =>
                {
                    job.IsRunning = true;
                    job.StatusKind = StatusKind.Info;
                    job.StatusMessage = "Generating image…";
                });

                await _runJob(job);

                switch (job.StatusKind)
                {
                    case StatusKind.Success: succeeded++; break;
                    case StatusKind.Canceled: canceled++; break;
                    default: failed++; break;
                }
            }

            var summary = $"Batch complete — {succeeded} ok, {failed} failed, {canceled} canceled.";
            var kind = (failed > 0 || canceled > 0) ? StatusKind.Warning : StatusKind.Success;
            _setStatus(summary, kind);
        }
        finally
        {
            _batchCts?.Dispose();
            _batchCts = null;
            IsBatchRunning = false;
        }
    }

    [RelayCommand]
    private void CancelBatch()
    {
        try { _batchCts?.Cancel(); }
        catch (ObjectDisposedException) { /* race with RunBatchAsync's finally */ }
    }
}
