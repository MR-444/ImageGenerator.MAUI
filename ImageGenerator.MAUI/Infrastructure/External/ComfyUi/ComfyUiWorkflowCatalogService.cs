using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

public sealed class ComfyUiWorkflowCatalogService : IComfyUiWorkflowCatalogService
{
    private readonly ILogger<ComfyUiWorkflowCatalogService> _logger;
    private readonly string _directory;

    public ComfyUiWorkflowCatalogService(
        ILogger<ComfyUiWorkflowCatalogService> logger,
        string? directoryOverride = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _directory = directoryOverride ?? OutputPaths.ComfyWorkflowsDirectory;
    }

    public Task<IReadOnlyList<ModelOption>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            // Ensure the folder exists so the user always has a place to drop exports into,
            // even before the first workflow.
            Directory.CreateDirectory(_directory);

            IReadOnlyList<ModelOption> options = Directory.EnumerateFiles(_directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(stem => !string.IsNullOrWhiteSpace(stem))
                .OrderBy(stem => stem, StringComparer.OrdinalIgnoreCase)
                .Select(stem => new ModelOption(
                    $"{stem} (ComfyUI)",
                    ModelConstants.ComfyUi.PrefixSlash + stem,
                    ProviderConstants.ComfyUi))
                .ToList();
            return Task.FromResult(options);
        }
        catch (Exception ex)
        {
            // IO failure (permissions, unavailable Pictures dir) must not break the catalog
            // for the other providers — same degrade-to-empty convention as Pollinations.
            _logger.LogWarning(ex, "ComfyUI workflow scan failed Directory={Directory}", _directory);
            return Task.FromResult<IReadOnlyList<ModelOption>>([]);
        }
    }
}
