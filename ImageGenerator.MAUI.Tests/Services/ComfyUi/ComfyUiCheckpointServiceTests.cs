using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiCheckpointServiceTests : IDisposable
{
    private readonly string _workflowDir;
    private readonly ComfyUiCheckpointService _service;

    public ComfyUiCheckpointServiceTests()
    {
        _workflowDir = Path.Combine(
            Path.GetTempPath(), "imggen-comfy-ckpt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workflowDir);

        _service = new ComfyUiCheckpointService(
            NullLogger<ComfyUiCheckpointService>.Instance,
            workflowsDirectoryOverride: _workflowDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workflowDir, recursive: true); } catch { /* best effort */ }
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

    // ---- GetWorkflowQualityPresetSlotAsync ---------------------------------------------------

    [Fact]
    public async Task PresetSlot_SingleComboTemplate_ReturnsBakedChoiceAndOptions()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "Combo Workflow.json"),
            """
            {
              "98:156": { "class_type": "CustomCombo",
                          "inputs": { "choice": "Default", "index": 1,
                                      "option1": "Quality", "option2": "Default",
                                      "option3": "Turbo", "option4": "" } },
              "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """);

        var slot = await _service.GetWorkflowQualityPresetSlotAsync("Combo Workflow");

        slot.Should().NotBeNull();
        slot!.BakedChoice.Should().Be("Default");
        slot.Options.Should().Equal("Quality", "Default", "Turbo");
    }

    [Fact]
    public async Task PresetSlot_MissingFileOrNoCombo_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "No Combo.json"),
            """{ "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } } }""");

        (await _service.GetWorkflowQualityPresetSlotAsync("does-not-exist")).Should().BeNull();
        (await _service.GetWorkflowQualityPresetSlotAsync("No Combo")).Should().BeNull();
    }

    // ---- GetWorkflowHasInputImageAsync / FindUpscaleWorkflowNameAsync ------------------------

    private const string LoadImageTemplate =
        """
        {
          "1": { "class_type": "LoadImage", "inputs": { "image": "input.png" } },
          "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
        }
        """;

    private const string TextToImageTemplate =
        """{ "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } } }""";

    [Fact]
    public async Task HasInputImage_TrueOnlyForLoadImageWorkflows()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "Img2Img.json"), LoadImageTemplate);
        File.WriteAllText(Path.Combine(_workflowDir, "Text2Img.json"), TextToImageTemplate);

        (await _service.GetWorkflowHasInputImageAsync("Img2Img")).Should().BeTrue();
        (await _service.GetWorkflowHasInputImageAsync("Text2Img")).Should().BeFalse();
        (await _service.GetWorkflowHasInputImageAsync("does-not-exist")).Should().BeFalse();
    }

    [Fact]
    public async Task FindUpscaleWorkflow_PicksAlphabeticallyFirstLoadImageBearingUpscaleStem()
    {
        // "AAA-Upscale" would win alphabetically but has no LoadImage — it must be skipped.
        File.WriteAllText(Path.Combine(_workflowDir, "AAA-Upscale.json"), TextToImageTemplate);
        File.WriteAllText(Path.Combine(_workflowDir, "Upscale-Sample.json"), LoadImageTemplate);
        File.WriteAllText(Path.Combine(_workflowDir, "ZZZ-upscale.json"), LoadImageTemplate);
        // LoadImage but no "upscale" in the stem: an edit workflow, never the chain target.
        File.WriteAllText(Path.Combine(_workflowDir, "Kontext-Edit.json"), LoadImageTemplate);

        (await _service.FindUpscaleWorkflowNameAsync()).Should().Be("Upscale-Sample");
    }

    [Fact]
    public async Task FindUpscaleWorkflow_NoQualifyingFile_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "Text2Img.json"), TextToImageTemplate);

        (await _service.FindUpscaleWorkflowNameAsync()).Should().BeNull();
    }
}
