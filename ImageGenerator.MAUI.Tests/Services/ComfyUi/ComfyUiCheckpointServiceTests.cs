using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiCheckpointServiceTests : IDisposable
{
    // Realistic /object_info/<class> shapes: the name input is [names, options-dict].
    private const string CheckpointObjectInfoBody =
        """
        {
          "CheckpointLoaderSimple": {
            "input": {
              "required": {
                "ckpt_name": [
                  ["a.safetensors", "b.safetensors"],
                  { "tooltip": "The name of the checkpoint (model) to load." }
                ]
              }
            },
            "output": ["MODEL", "CLIP", "VAE"]
          }
        }
        """;

    private const string UnetObjectInfoBody =
        """
        {
          "UNETLoader": {
            "input": {
              "required": {
                "unet_name": [
                  ["flux-dev.safetensors", "ideogram4_fp8_scaled.safetensors"],
                  { "tooltip": "The name of the diffusion model to load." }
                ],
                "weight_dtype": [["default", "fp8_e4m3fn"]]
              }
            },
            "output": ["MODEL"]
          }
        }
        """;

    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Loose);
    private readonly Mock<IUiStateStore> _uiState = new();
    private readonly Mock<IComfyUiAuthStore> _authStore = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly string _cacheDir;
    private readonly string _workflowDir;
    private readonly ComfyUiCheckpointService _service;

    private Func<HttpResponseMessage> _response = () => Json(CheckpointObjectInfoBody);

    public ComfyUiCheckpointServiceTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "imggen-comfy-ckpt-tests-" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(root, "cache");
        _workflowDir = Path.Combine(root, "workflows");
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_workflowDir);

        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("http://test-host:8188");
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);

        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _requests.Add(req))
            .ReturnsAsync((HttpRequestMessage _, CancellationToken _) => _response());

        _service = new ComfyUiCheckpointService(
            new StubHttpClientFactory(() => new HttpClient(_handler.Object)),
            _uiState.Object,
            _authStore.Object,
            NullLogger<ComfyUiCheckpointService>.Instance,
            cacheDirectoryOverride: _cacheDir,
            workflowsDirectoryOverride: _workflowDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_cacheDir)!, recursive: true); } catch { /* best effort */ }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new()
    {
        StatusCode = status,
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private string CheckpointCachePath => Path.Combine(_cacheDir, "comfyui-checkpoints.json");
    private string UnetCachePath => Path.Combine(_cacheDir, "comfyui-unets.json");

    private void SeedCache(string path, params string[] names) =>
        File.WriteAllText(path, JsonSerializer.Serialize(names));

    // ---- GetModelNamesAsync(Checkpoint) ----------------------------------------------------

    [Fact]
    public async Task Checkpoints_RequestsNarrowObjectInfoEndpoint_AndParsesNames()
    {
        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        names.Should().Equal("a.safetensors", "b.safetensors");
        var request = _requests.Single();
        request.RequestUri!.AbsolutePath.Should().Be("/object_info/CheckpointLoaderSimple",
            "the per-class endpoint avoids the multi-MB full /object_info dump");
        request.RequestUri.Host.Should().Be("test-host", "base URL comes from the UI state store");
    }

    [Fact]
    public async Task Checkpoints_OnSuccess_WritesAtomicCacheFile()
    {
        await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        File.Exists(CheckpointCachePath).Should().BeTrue();
        File.Exists(CheckpointCachePath + ".tmp").Should().BeFalse("the temp file must be moved, not left behind");
        JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CheckpointCachePath))
            .Should().Equal("a.safetensors", "b.safetensors");
    }

    [Fact]
    public async Task Checkpoints_ServerError_FallsBackToCachedList()
    {
        SeedCache(CheckpointCachePath, "cached.safetensors");
        _response = () => Json("oops", HttpStatusCode.InternalServerError);

        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        names.Should().Equal("cached.safetensors");
    }

    [Fact]
    public async Task Checkpoints_ConnectionFailure_WithoutCache_ReturnsNull()
    {
        _response = () => throw new HttpRequestException("connection refused");

        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        names.Should().BeNull();
    }

    [Fact]
    public async Task Checkpoints_MalformedObjectInfo_FallsBackToCache()
    {
        SeedCache(CheckpointCachePath, "cached.safetensors");
        _response = () => Json("""{ "CheckpointLoaderSimple": { "input": { "required": {} } } }""");

        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        names.Should().Equal("cached.safetensors");
    }

    [Fact]
    public async Task Checkpoints_InvalidBaseUrl_ReturnsCacheWithoutHttpCall()
    {
        SeedCache(CheckpointCachePath, "cached.safetensors");
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("not a url");

        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        names.Should().Equal("cached.safetensors");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Checkpoints_AuthHeaderSet_ObjectInfoRequestCarriesIt()
    {
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync("Bearer abc123");

        await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        var request = _requests.Single();
        request.Headers.TryGetValues("Authorization", out var values).Should().BeTrue();
        values!.Single().Should().Be("Bearer abc123");
    }

    [Fact]
    public async Task Checkpoints_AuthHeaderUnset_ObjectInfoRequestHasNone()
    {
        await _service.GetModelNamesAsync(ComfyUiLoaderKind.Checkpoint);

        _requests.Single().Headers.Contains("Authorization").Should().BeFalse();
    }

    // ---- GetModelNamesAsync(Unet) ----------------------------------------------------------

    [Fact]
    public async Task Unets_RequestsUnetLoaderEndpoint_AndParsesUnetNames()
    {
        _response = () => Json(UnetObjectInfoBody);

        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Unet);

        names.Should().Equal("flux-dev.safetensors", "ideogram4_fp8_scaled.safetensors");
        _requests.Single().RequestUri!.AbsolutePath.Should().Be("/object_info/UNETLoader");
    }

    [Fact]
    public async Task Unets_WriteTheirOwnCacheFile_IndependentOfCheckpoints()
    {
        _response = () => Json(UnetObjectInfoBody);

        await _service.GetModelNamesAsync(ComfyUiLoaderKind.Unet);

        JsonSerializer.Deserialize<List<string>>(File.ReadAllText(UnetCachePath))
            .Should().Equal("flux-dev.safetensors", "ideogram4_fp8_scaled.safetensors");
        File.Exists(CheckpointCachePath).Should().BeFalse("the kinds' caches must not bleed into each other");
    }

    [Fact]
    public async Task Unets_FetchFailure_FallsBackToUnetCacheOnly()
    {
        // The other kind's cache must never satisfy a Unet request — those are different
        // model folders on the server.
        SeedCache(CheckpointCachePath, "checkpoint-only.safetensors");
        SeedCache(UnetCachePath, "cached-unet.safetensors");
        _response = () => throw new HttpRequestException("connection refused");

        var names = await _service.GetModelNamesAsync(ComfyUiLoaderKind.Unet);

        names.Should().Equal("cached-unet.safetensors");
    }

    // ---- GetWorkflowModelSlotAsync -----------------------------------------------------------

    [Fact]
    public async Task WorkflowSlot_CheckpointTemplate_ReturnsCheckpointKind()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "My Workflow.json"),
            """
            {
              "4": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "baked.safetensors" } },
              "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """);

        var slot = await _service.GetWorkflowModelSlotAsync("My Workflow");

        slot.Should().Be(new ComfyUiModelSlot(ComfyUiLoaderKind.Checkpoint, "baked.safetensors"));
    }

    [Fact]
    public async Task WorkflowSlot_SingleUnetTemplate_ReturnsUnetKind()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "Unet Workflow.json"),
            """
            {
              "10": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux-dev.safetensors" } },
              "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """);

        var slot = await _service.GetWorkflowModelSlotAsync("Unet Workflow");

        slot.Should().Be(new ComfyUiModelSlot(ComfyUiLoaderKind.Unet, "flux-dev.safetensors"));
    }

    [Fact]
    public async Task WorkflowSlot_MissingFileOrNoLoader_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "No Loader.json"),
            """{ "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } } }""");

        (await _service.GetWorkflowModelSlotAsync("does-not-exist")).Should().BeNull();
        (await _service.GetWorkflowModelSlotAsync("No Loader")).Should().BeNull();
    }
}
