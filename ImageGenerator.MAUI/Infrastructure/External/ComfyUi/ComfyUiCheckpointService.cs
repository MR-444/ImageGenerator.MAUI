using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Probes workflow template files for the slots the generator UI surfaces: the baked-in
/// model (read-only display) and the CustomCombo quality preset. Pure file reads — the
/// probes themselves live in <see cref="ComfyUiWorkflowPatcher"/>.
/// </summary>
public sealed class ComfyUiCheckpointService : IComfyUiCheckpointService
{
    private readonly ILogger<ComfyUiCheckpointService> _logger;
    private readonly string _workflowsDirectory;

    public ComfyUiCheckpointService(
        ILogger<ComfyUiCheckpointService> logger,
        string? workflowsDirectoryOverride = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workflowsDirectory = workflowsDirectoryOverride ?? OutputPaths.ComfyWorkflowsDirectory;
    }

    public async Task<ComfyUiModelSlot?> GetWorkflowModelSlotAsync(string workflowName, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_workflowsDirectory, workflowName + ".json");
            if (!File.Exists(path)) return null;

            var template = await File.ReadAllTextAsync(path, ct);
            return ComfyUiWorkflowPatcher.FindBakedModelSlot(template);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI workflow model-slot probe failed Workflow={Workflow}", workflowName);
            return null;
        }
    }

    public async Task<ComfyUiQualityPresetSlot?> GetWorkflowQualityPresetSlotAsync(
        string workflowName, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_workflowsDirectory, workflowName + ".json");
            if (!File.Exists(path)) return null;

            var template = await File.ReadAllTextAsync(path, ct);
            return ComfyUiWorkflowPatcher.FindQualityPresetSlot(template);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI workflow preset-slot probe failed Workflow={Workflow}", workflowName);
            return null;
        }
    }

    public async Task<bool> GetWorkflowHasInputImageAsync(string workflowName, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_workflowsDirectory, workflowName + ".json");
            if (!File.Exists(path)) return false;

            var template = await File.ReadAllTextAsync(path, ct);
            return ComfyUiWorkflowPatcher.HasLoadImageNode(template);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI workflow input-image probe failed Workflow={Workflow}", workflowName);
            return false;
        }
    }

    public async Task<double?> GetWorkflowUpscaleFactorAsync(string workflowName, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_workflowsDirectory, workflowName + ".json");
            if (!File.Exists(path)) return null;

            var template = await File.ReadAllTextAsync(path, ct);
            return ComfyUiWorkflowPatcher.FindUpscaleFactorSlot(template);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI workflow upscale-factor probe failed Workflow={Workflow}", workflowName);
            return null;
        }
    }

    public async Task<string?> FindUpscaleWorkflowNameAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(_workflowsDirectory)) return null;

            // "upscale" in the stem is the designation; LoadImage confirms the file can
            // actually receive the rendered image. Alphabetically first wins so the pick is
            // deterministic when several qualify.
            var candidates = Directory.EnumerateFiles(_workflowsDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(stem => stem?.Contains("upscale", StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(stem => stem, StringComparer.OrdinalIgnoreCase);

            foreach (var stem in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (await GetWorkflowHasInputImageAsync(stem!, ct)) return stem;
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI upscale-workflow scan failed Directory={Directory}", _workflowsDirectory);
            return null;
        }
    }
}
