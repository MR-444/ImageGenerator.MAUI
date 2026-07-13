using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;
using Xunit;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

public class ComfyUiWorkflowPatcherTests
{
    // Shape mirrors the user's "Ideogram workflow_MR" API export: a ResolutionSelector feeding
    // the builder via links, import_json WIRED (link), a RandomNoise seed, a SaveImage — and,
    // crucially, the builder acting as a VIEWER while the REAL prompt is a frozen caption-JSON
    // literal on a flattened-subgraph CLIPTextEncode ("98:24").
    private const string IdeogramTemplate =
        """
        {
          "37":  { "class_type": "ResolutionSelector",
                   "inputs": { "aspect_ratio": "4:3 (Standard)", "megapixels": 1.5 } },
          "165": { "class_type": "RandomNoise",
                   "inputs": { "noise_seed": 1335735769456 } },
          "179": { "class_type": "Ideogram4PromptBuilderKJ",
                   "inputs": { "width": ["37", 0], "height": ["37", 1],
                               "import_json": ["98", 1], "import_mode": "when empty",
                               "background": "old background" } },
          "190": { "class_type": "KSampler",
                   "inputs": { "seed": 42, "steps": 28 } },
          "98:24": { "class_type": "CLIPTextEncode",
                     "inputs": { "text": "{ \"high_level_description\": \"OLD caption\" }",
                                 "clip": ["98:14", 0] } },
          "200": { "class_type": "SaveImage",
                   "inputs": { "images": ["162", 0], "filename_prefix": "Ideogram4" } }
        }
        """;

    // No literal text anywhere — every encoder is link-driven.
    private const string LinkOnlyTemplate =
        """
        {
          "2": { "class_type": "CLIPTextEncode", "inputs": { "text": ["9", 0] } },
          "3": { "class_type": "KSampler", "inputs": { "seed": 7 } }
        }
        """;

    private const string PlainTemplate =
        """
        {
          "3": { "class_type": "KSampler", "inputs": { "seed": 7, "denoise": 1.0 } },
          "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } },
          "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "old negative" } }
        }
        """;

    // Two literal loaders (out of id order to pin the lowest-id rule) plus a link-driven one
    // that must never be touched.
    private const string CheckpointTemplate =
        """
        {
          "12": { "class_type": "CheckpointLoaderSimple",
                  "inputs": { "ckpt_name": "refiner.safetensors" } },
          "4":  { "class_type": "CheckpointLoaderSimple",
                  "inputs": { "ckpt_name": "baked.safetensors" } },
          "9":  { "class_type": "CheckpointLoaderSimple",
                  "inputs": { "ckpt_name": ["20", 0] } },
          "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } },
          "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } }
        }
        """;

    private static ComfyUiRequest Request(
        string prompt = "p", bool json = false, long seed = 123,
        string? ar = null, double? mp = null, string? ckpt = null, string? preset = null) =>
        new("wf", prompt, json, seed, ar, mp, ckpt, preset);

    [Fact]
    public void JsonMode_PatchesImportJsonAndMode_OverwritingTheWiredLink()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            IdeogramTemplate, Request(prompt: """{"high_level_description":"x"}""", json: true));

        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();
        var builder = graph["179"]!["inputs"]!;
        builder["import_json"]!.GetValue<string>().Should().Be("""{"high_level_description":"x"}""");
        builder["import_mode"]!.GetValue<string>().Should().Be("always");
        result.PromptTargetDescription.Should().Contain("Ideogram4PromptBuilderKJ");
    }

    [Fact]
    public void JsonMode_AlsoReplacesCaptionJsonLiteralTextEncodes()
    {
        // The live-bug shape: the builder is a viewer; the conditioning comes from the frozen
        // caption-JSON literal on the flattened-subgraph CLIPTextEncode. It must be replaced.
        var result = ComfyUiWorkflowPatcher.Patch(
            IdeogramTemplate, Request(prompt: """{"high_level_description":"NEW"}""", json: true));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["98:24"]!["inputs"]!["text"]!.GetValue<string>()
            .Should().Be("""{"high_level_description":"NEW"}""");
        graph["98:24"]!["inputs"]!["clip"]!.GetValueKind().Should().Be(JsonValueKind.Array);
        result.PromptTargetDescription.Should().Contain("1 JSON-literal CLIPTextEncode");
    }

    [Fact]
    public void JsonMode_LeavesPlainTextLiteralEncodesAlone()
    {
        const string withNegative =
            """
            {
              "5":   { "class_type": "Ideogram4PromptBuilderKJ", "inputs": { "import_mode": "when empty" } },
              "7":   { "class_type": "CLIPTextEncode", "inputs": { "text": "blurry, lowres" } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(
            withNegative, Request(prompt: """{"a":1}""", json: true));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["7"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("blurry, lowres",
            "non-JSON literals (plain/negative prompts) are not caption sinks");
    }

    [Fact]
    public void JsonMode_LeavesLinkedWidthHeightAndOtherInputsUntouched()
    {
        var result = ComfyUiWorkflowPatcher.Patch(IdeogramTemplate, Request(json: true));

        var builder = JsonNode.Parse(result.GraphJson)!["179"]!["inputs"]!;
        builder["width"]!.AsArray()[0]!.GetValue<string>().Should().Be("37");
        builder["height"]!.AsArray()[1]!.GetValue<int>().Should().Be(1);
        builder["background"]!.GetValue<string>().Should().Be("old background");
    }

    [Fact]
    public void JsonMode_WithoutBuilderNode_ThrowsWithGuidance()
    {
        var act = () => ComfyUiWorkflowPatcher.Patch(PlainTemplate, Request(json: true));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Ideogram4PromptBuilderKJ*");
    }

    [Fact]
    public void PlainMode_PatchesOnlyTheLowestIdLiteralTextEncode()
    {
        var result = ComfyUiWorkflowPatcher.Patch(PlainTemplate, Request(prompt: "new prompt"));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["6"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("new prompt");
        graph["7"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("old negative");
        result.PromptTargetDescription.Should().Contain("node 6");
    }

    [Fact]
    public void PlainMode_OnLinkDrivenWorkflow_ThrowsAndPointsToJsonMode()
    {
        var act = () => ComfyUiWorkflowPatcher.Patch(LinkOnlyTemplate, Request(json: false));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Structured JSON*");
    }

    [Fact]
    public void PlainMode_OnTheIdeogramTemplate_PatchesTheCaptionLiteral()
    {
        // The subgraph encode's literal IS the prompt sink, JSON or not — plain mode hits it.
        var result = ComfyUiWorkflowPatcher.Patch(IdeogramTemplate, Request(prompt: "plain", json: false));

        JsonNode.Parse(result.GraphJson)!["98:24"]!["inputs"]!["text"]!
            .GetValue<string>().Should().Be("plain");
    }

    [Fact]
    public void PlainMode_NeverPatchesALinkedTextInput()
    {
        const string linked =
            """
            {
              "2": { "class_type": "CLIPTextEncode", "inputs": { "text": ["9", 0] } },
              "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "literal" } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(linked, Request(prompt: "new"));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["2"]!["inputs"]!["text"]!.GetValueKind().Should().Be(JsonValueKind.Array);
        graph["5"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("new");
    }

    [Fact]
    public void Seeds_AllLiteralSeedAndNoiseSeedInputsAreRerolled()
    {
        var result = ComfyUiWorkflowPatcher.Patch(IdeogramTemplate, Request(json: true, seed: 999));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["165"]!["inputs"]!["noise_seed"]!.GetValue<long>().Should().Be(999);
        graph["190"]!["inputs"]!["seed"]!.GetValue<long>().Should().Be(999);
        result.SeedNodeIds.Should().BeEquivalentTo(["165", "190"]);
    }

    [Fact]
    public void Seeds_LinkedOrStringSeedInputsStayUntouched()
    {
        const string template =
            """
            {
              "1": { "class_type": "KSampler", "inputs": { "seed": ["8", 0] } },
              "2": { "class_type": "Whatever", "inputs": { "seed": "not-a-number" } },
              "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(template, Request(seed: 999));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["1"]!["inputs"]!["seed"]!.GetValueKind().Should().Be(JsonValueKind.Array);
        graph["2"]!["inputs"]!["seed"]!.GetValue<string>().Should().Be("not-a-number");
        result.SeedNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolution_PatchesAspectRatioAndMegapixelsOnSelectorNodes()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            IdeogramTemplate, Request(json: true, ar: "9:16 (Portrait Widescreen)", mp: 2.0));

        var selector = JsonNode.Parse(result.GraphJson)!["37"]!["inputs"]!;
        selector["aspect_ratio"]!.GetValue<string>().Should().Be("9:16 (Portrait Widescreen)");
        selector["megapixels"]!.GetValue<double>().Should().Be(2.0);
        result.PromptTargetDescription.Should().Contain("ResolutionSelector");
    }

    [Fact]
    public void Resolution_NullValuesLeaveTheSelectorUntouched()
    {
        var result = ComfyUiWorkflowPatcher.Patch(IdeogramTemplate, Request(json: true));

        var selector = JsonNode.Parse(result.GraphJson)!["37"]!["inputs"]!;
        selector["aspect_ratio"]!.GetValue<string>().Should().Be("4:3 (Standard)");
        selector["megapixels"]!.GetValue<double>().Should().Be(1.5);
    }

    [Fact]
    public void Resolution_WithoutSelectorNode_SkipsSilentlyAndNotesIt()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            PlainTemplate, Request(ar: "1:1 (Square)", mp: 1.0));

        result.PromptTargetDescription.Should().Contain("no ResolutionSelector");
    }

    [Fact]
    public void UiFormatSave_ThrowsWithExportApiGuidance()
    {
        const string uiFormat = """{ "nodes": [ { "id": 1 } ], "links": [] }""";

        var act = () => ComfyUiWorkflowPatcher.Patch(uiFormat, Request());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Export (API)*");
    }

    [Fact]
    public void MalformedJson_ThrowsJsonException()
    {
        var act = () => ComfyUiWorkflowPatcher.Patch("{ not json", Request());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void EmptyGraph_Throws()
    {
        var act = () => ComfyUiWorkflowPatcher.Patch("""{ "extra": 1 }""", Request());

        act.Should().Throw<InvalidOperationException>().WithMessage("*no nodes*");
    }

    [Fact]
    public void Output_IsValidJsonAndPreservesUnrelatedNodes()
    {
        var result = ComfyUiWorkflowPatcher.Patch(IdeogramTemplate, Request(json: true));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["200"]!["inputs"]!["filename_prefix"]!.GetValue<string>().Should().Be("Ideogram4");
        graph["200"]!["inputs"]!["images"]!.GetValueKind().Should().Be(JsonValueKind.Array);
    }

    // ---- checkpoint patching -------------------------------------------------------------

    [Fact]
    public void Checkpoint_Null_LeavesCkptNameUntouched()
    {
        var result = ComfyUiWorkflowPatcher.Patch(CheckpointTemplate, Request());

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>().Should().Be("baked.safetensors");
        graph["12"]!["inputs"]!["ckpt_name"]!.GetValue<string>().Should().Be("refiner.safetensors");
        result.PromptTargetDescription.Should().NotContain("ckpt_name").And.NotContain("checkpoint");
    }

    [Fact]
    public void Checkpoint_PatchesAllLiteralCkptNameLoaders()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            CheckpointTemplate, Request(ckpt: "server.safetensors"));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>().Should().Be("server.safetensors");
        graph["12"]!["inputs"]!["ckpt_name"]!.GetValue<string>().Should().Be("server.safetensors");
        result.PromptTargetDescription.Should().Contain("ckpt_name on 2 CheckpointLoaderSimple node(s)");
    }

    [Fact]
    public void Checkpoint_LeavesLinkDrivenCkptNameUntouched()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            CheckpointTemplate, Request(ckpt: "server.safetensors"));

        JsonNode.Parse(result.GraphJson)!["9"]!["inputs"]!["ckpt_name"]!
            .GetValueKind().Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void Checkpoint_WithoutLoaderNode_SkipsSilentlyAndNotesIt()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            PlainTemplate, Request(ckpt: "server.safetensors"));

        result.PromptTargetDescription.Should().Contain("no unambiguous model loader");
    }

    [Fact]
    public void FindBakedModelSlot_CheckpointTemplate_ReturnsLowestIdLiteralAsCheckpointKind()
    {
        ComfyUiWorkflowPatcher.FindBakedModelSlot(CheckpointTemplate)
            .Should().Be(new ComfyUiModelSlot(ComfyUiLoaderKind.Checkpoint, "baked.safetensors"));
    }

    [Fact]
    public void FindBakedModelSlot_LinkOnlyOrAbsentLoader_ReturnsNull()
    {
        const string linkOnlyLoader =
            """
            {
              "4": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": ["20", 0] } },
              "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """;

        ComfyUiWorkflowPatcher.FindBakedModelSlot(linkOnlyLoader).Should().BeNull();
        ComfyUiWorkflowPatcher.FindBakedModelSlot(PlainTemplate).Should().BeNull();
    }

    [Fact]
    public void FindBakedModelSlot_InvalidJsonOrUiFormat_ReturnsNullWithoutThrowing()
    {
        ComfyUiWorkflowPatcher.FindBakedModelSlot("{ not json").Should().BeNull();
        ComfyUiWorkflowPatcher.FindBakedModelSlot("""{ "nodes": [ { "id": 1 } ] }""").Should().BeNull();
    }

    // ---- UNETLoader (split-loader workflows) ----------------------------------------------
    // Mirrors the user's real Ideogram graph: when TWO UNETLoaders hold DIFFERENT models
    // (conditional + unconditional feeding a DualModelGuider), the pair is deliberate and
    // must never be half-swapped — no slot, no patch. Only an exactly-one literal UNETLoader
    // qualifies as the workflow's swappable model.

    // One literal UNETLoader; the link-driven second one must not count toward the
    // exactly-one rule.
    private const string SingleUnetTemplate =
        """
        {
          "10": { "class_type": "UNETLoader",
                  "inputs": { "unet_name": "flux-dev.safetensors", "weight_dtype": "default" } },
          "11": { "class_type": "UNETLoader",
                  "inputs": { "unet_name": ["20", 0], "weight_dtype": "default" } },
          "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } },
          "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
        }
        """;

    private const string DualUnetTemplate =
        """
        {
          "10": { "class_type": "UNETLoader",
                  "inputs": { "unet_name": "ideogram4_fp8_scaled.safetensors", "weight_dtype": "default" } },
          "12": { "class_type": "UNETLoader",
                  "inputs": { "unet_name": "ideogram4_unconditional_fp8_scaled.safetensors", "weight_dtype": "default" } },
          "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } },
          "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
        }
        """;

    [Fact]
    public void FindBakedModelSlot_SingleLiteralUnet_ReturnsUnetKind()
    {
        ComfyUiWorkflowPatcher.FindBakedModelSlot(SingleUnetTemplate)
            .Should().Be(new ComfyUiModelSlot(ComfyUiLoaderKind.Unet, "flux-dev.safetensors"));
    }

    [Fact]
    public void FindBakedModelSlot_TwoLiteralUnets_ReturnsNull()
    {
        ComfyUiWorkflowPatcher.FindBakedModelSlot(DualUnetTemplate).Should().BeNull();
    }

    [Fact]
    public void FindBakedModelSlot_CheckpointWinsOverUnet()
    {
        const string bothKinds =
            """
            {
              "4":  { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "baked.safetensors" } },
              "10": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux-dev.safetensors" } },
              "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """;

        ComfyUiWorkflowPatcher.FindBakedModelSlot(bothKinds)
            .Should().Be(new ComfyUiModelSlot(ComfyUiLoaderKind.Checkpoint, "baked.safetensors"));
    }

    [Fact]
    public void Unet_SingleLiteralLoader_PatchesUnetName()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SingleUnetTemplate, Request(ckpt: "other-model.safetensors"));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["10"]!["inputs"]!["unet_name"]!.GetValue<string>().Should().Be("other-model.safetensors");
        graph["11"]!["inputs"]!["unet_name"]!.GetValueKind().Should().Be(JsonValueKind.Array,
            "link-driven loaders are never touched");
        result.PromptTargetDescription.Should().Contain("unet_name on UNETLoader node 10");
    }

    [Fact]
    public void Unet_TwoLiteralLoaders_LeavesBothUntouchedAndNotesIt()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            DualUnetTemplate, Request(ckpt: "other-model.safetensors"));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["10"]!["inputs"]!["unet_name"]!.GetValue<string>().Should().Be("ideogram4_fp8_scaled.safetensors");
        graph["12"]!["inputs"]!["unet_name"]!.GetValue<string>().Should().Be("ideogram4_unconditional_fp8_scaled.safetensors");
        result.PromptTargetDescription.Should().Contain("no unambiguous model loader");
    }

    // ---- SageAttention runtime injection ------------------------------------------------

    private const string DualSageTemplate =
        """
        {
          "emberforge:sage:1": { "class_type": "Note", "inputs": {} },
          "98:23":  { "class_type": "UNETLoader", "inputs": { "unet_name": "base.safetensors" } },
          "98:154": { "class_type": "UNETLoader", "inputs": { "unet_name": "negative.safetensors" } },
          "98:157": { "class_type": "LoraLoaderModelOnly", "inputs": { "model": ["98:23", 0] } },
          "98:300": { "class_type": "Epsilon Scaling", "inputs": { "model": ["98:23", 0] } },
          "98:155": { "class_type": "DualModelGuider",
                      "inputs": { "model": ["98:157", 0], "model_negative": ["98:154", 0] } },
          "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } }
        }
        """;

    [Fact]
    public void SageAttention_Disabled_LeavesModelEdgesAndResultMetadataUntouched()
    {
        var result = ComfyUiWorkflowPatcher.Patch(DualSageTemplate, Request());
        var graph = JsonNode.Parse(result.GraphJson)!;

        graph["98:157"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("98:23");
        graph.AsObject().Any(node =>
            node.Value?["class_type"]?.GetValue<string>() == "PathchSageAttentionKJ")
            .Should().BeFalse();
        result.SageAttentionNodeIds.Should().BeEmpty();
        result.SageAttentionLoaderIds.Should().BeEmpty();
    }

    [Fact]
    public void SageAttention_DualUnet_NonnumericIds_PatchesEveryConsumerBeforeLoraAndGuider()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            DualSageTemplate, Request(), useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();

        result.SageAttentionLoaderIds.Should().BeEquivalentTo(["98:23", "98:154"]);
        result.SageAttentionNodeIds.Should().Equal(
            ["emberforge:sage:2", "emberforge:sage:3"],
            "the existing emberforge:sage:1 key forces deterministic collision avoidance");

        var baseSageEntry = graph.Single(node =>
            node.Value?["class_type"]?.GetValue<string>() == "PathchSageAttentionKJ"
            && node.Value["inputs"]!["model"]![0]!.GetValue<string>() == "98:23");
        var baseSage = baseSageEntry.Value!;
        baseSage["class_type"]!.GetValue<string>().Should().Be("PathchSageAttentionKJ");
        baseSage["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("98:23");
        baseSage["inputs"]!["sage_attention"]!.GetValue<string>().Should().Be("auto");
        graph["98:157"]!["inputs"]!["model"]![0]!.GetValue<string>()
            .Should().Be(baseSageEntry.Key, "Sage must sit before the LoRA loader");
        graph["98:300"]!["inputs"]!["model"]![0]!.GetValue<string>()
            .Should().Be(baseSageEntry.Key, "all consumers share one patch per loader");

        var negativeSageId = graph.Single(node =>
            node.Value?["class_type"]?.GetValue<string>() == "PathchSageAttentionKJ"
            && node.Value["inputs"]!["model"]![0]!.GetValue<string>() == "98:154").Key;
        graph["98:155"]!["inputs"]!["model_negative"]![0]!.GetValue<string>()
            .Should().Be(negativeSageId);
        result.PromptTargetDescription.Should().Contain("SageAttention on 2 model loader(s)");
    }

    [Theory]
    [InlineData("CheckpointLoaderSimple")]
    [InlineData("UNETLoader")]
    [InlineData("UnetLoaderGGUF")]
    public void SageAttention_RecognizesEverySupportedLoaderClass(string loaderClass)
    {
        var template =
            $$"""
            {
              "loader": { "class_type": "{{loaderClass}}", "inputs": {} },
              "consumer": { "class_type": "ModelConsumer", "inputs": { "model": ["loader", 0] } },
              "text": { "class_type": "CLIPTextEncode", "inputs": { "text": "old" } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), useSageAttention: true);

        result.SageAttentionLoaderIds.Should().Equal("loader");
        result.SageAttentionNodeIds.Should().ContainSingle();
    }

    [Fact]
    public void SageAttention_Checkpoint_RewiresOnlyModelSlot_LeavingClipAndVaeLinksUntouched()
    {
        const string template =
            """
            {
              "4": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "model.safetensors" } },
              "5": { "class_type": "ModelConsumer", "inputs": { "model": ["4", 0] } },
              "6": { "class_type": "CLIPTextEncode", "inputs": { "clip": ["4", 1], "text": "old" } },
              "7": { "class_type": "VAEConsumer", "inputs": { "vae": ["4", 2] } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!;
        var sageId = result.SageAttentionNodeIds.Single();

        graph["5"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be(sageId);
        graph["6"]!["inputs"]!["clip"]![0]!.GetValue<string>().Should().Be("4");
        graph["6"]!["inputs"]!["clip"]![1]!.GetValue<int>().Should().Be(1);
        graph["7"]!["inputs"]!["vae"]![0]!.GetValue<string>().Should().Be("4");
        graph["7"]!["inputs"]!["vae"]![1]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void SageAttention_ExistingPatch_IsCoveredWithoutDoubleWrappingAndKeepsItsMode()
    {
        const string template =
            """
            {
              "4": { "class_type": "UNETLoader", "inputs": { "unet_name": "model.safetensors" } },
              "5": { "class_type": "PathchSageAttentionKJ",
                     "inputs": { "model": ["4", 0], "sage_attention": "sageattn3" } },
              "6": { "class_type": "ModelConsumer", "inputs": { "model": ["5", 0] } },
              "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "old" } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();

        result.SageAttentionNodeIds.Should().Equal("5");
        result.SageAttentionLoaderIds.Should().Equal("4");
        graph.Count(node => node.Value?["class_type"]?.GetValue<string>() == "PathchSageAttentionKJ")
            .Should().Be(1);
        graph["5"]!["inputs"]!["sage_attention"]!.GetValue<string>().Should().Be("sageattn3");
    }

    [Fact]
    public void SageAttention_EnabledWithoutSupportedModelEdge_FailsClearly()
    {
        var act = () => ComfyUiWorkflowPatcher.Patch(
            PlainTemplate, Request(), useSageAttention: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no supported MODEL edge*turn off*Use SageAttention*");
    }

    // ---- CustomCombo quality preset --------------------------------------------------------
    // Mirrors the Ideogram4 sample's node 98:156: choice + 0-based index over option1..option4
    // (empty = unused slot). The class is generic, so only an EXACTLY-ONE CustomCombo graph
    // exposes a slot; probe and patch share the targeting so offer and patch always agree.

    private const string ComboTemplate =
        """
        {
          "98:156": { "class_type": "CustomCombo",
                      "inputs": { "choice": "Default", "index": 1,
                                  "option1": "Quality", "option2": "Default",
                                  "option3": "Turbo", "option4": "" } },
          "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } },
          "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
        }
        """;

    private const string DualComboTemplate =
        """
        {
          "20": { "class_type": "CustomCombo",
                  "inputs": { "choice": "Default", "index": 1,
                              "option1": "Quality", "option2": "Default", "option3": "", "option4": "" } },
          "21": { "class_type": "CustomCombo",
                  "inputs": { "choice": "euler", "index": 0,
                              "option1": "euler", "option2": "dpmpp_2m", "option3": "", "option4": "" } },
          "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } },
          "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
        }
        """;

    [Fact]
    public void FindQualityPresetSlot_SingleCombo_ReturnsBakedChoiceAndNonEmptyOptions()
    {
        var slot = ComfyUiWorkflowPatcher.FindQualityPresetSlot(ComboTemplate);

        slot.Should().NotBeNull();
        slot!.BakedChoice.Should().Be("Default");
        slot.Options.Should().Equal("Quality", "Default", "Turbo");
    }

    [Fact]
    public void FindQualityPresetSlot_NoCombo_ReturnsNull()
    {
        ComfyUiWorkflowPatcher.FindQualityPresetSlot(PlainTemplate).Should().BeNull();
    }

    [Fact]
    public void FindQualityPresetSlot_TwoCombos_ReturnsNull()
    {
        // The class is generic (the second combo here picks a sampler) — with two of them the
        // quality target is ambiguous, so neither is offered.
        ComfyUiWorkflowPatcher.FindQualityPresetSlot(DualComboTemplate).Should().BeNull();
    }

    [Fact]
    public void FindQualityPresetSlot_LinkDrivenChoice_ReturnsNull()
    {
        const string linkDriven =
            """
            {
              "20": { "class_type": "CustomCombo",
                      "inputs": { "choice": ["9", 0], "index": 1,
                                  "option1": "Quality", "option2": "Default", "option3": "", "option4": "" } },
              "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
            }
            """;

        ComfyUiWorkflowPatcher.FindQualityPresetSlot(linkDriven).Should().BeNull();
    }

    [Fact]
    public void FindQualityPresetSlot_AllOptionsEmpty_ReturnsNull()
    {
        const string optionless =
            """
            {
              "20": { "class_type": "CustomCombo",
                      "inputs": { "choice": "x", "index": 0,
                                  "option1": "", "option2": "", "option3": "", "option4": "" } },
              "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
            }
            """;

        ComfyUiWorkflowPatcher.FindQualityPresetSlot(optionless).Should().BeNull();
    }

    [Fact]
    public void FindQualityPresetSlot_InvalidJsonOrUiFormat_ReturnsNullWithoutThrowing()
    {
        ComfyUiWorkflowPatcher.FindQualityPresetSlot("not json at all").Should().BeNull();
        ComfyUiWorkflowPatcher.FindQualityPresetSlot("""{ "nodes": [ { "id": 1 } ] }""").Should().BeNull();
    }

    [Fact]
    public void Preset_PatchesChoiceAndMatchingZeroBasedIndex()
    {
        var result = ComfyUiWorkflowPatcher.Patch(ComboTemplate, Request(preset: "Turbo"));

        var inputs = JsonNode.Parse(result.GraphJson)!["98:156"]!["inputs"]!;
        inputs["choice"]!.GetValue<string>().Should().Be("Turbo");
        inputs["index"]!.GetValue<int>().Should().Be(2, "Turbo is option3 = slot position 2 (0-based)");
        result.PromptTargetDescription.Should().Contain("choice+index on CustomCombo node 98:156");
    }

    [Fact]
    public void Preset_Null_LeavesComboUntouched()
    {
        var result = ComfyUiWorkflowPatcher.Patch(ComboTemplate, Request());

        var inputs = JsonNode.Parse(result.GraphJson)!["98:156"]!["inputs"]!;
        inputs["choice"]!.GetValue<string>().Should().Be("Default");
        inputs["index"]!.GetValue<int>().Should().Be(1);
        result.PromptTargetDescription.Should().NotContain("CustomCombo");
    }

    [Fact]
    public void Preset_NotAmongOptions_LeavesComboUntouchedAndNotesIt()
    {
        var result = ComfyUiWorkflowPatcher.Patch(ComboTemplate, Request(preset: "Ludicrous"));

        var inputs = JsonNode.Parse(result.GraphJson)!["98:156"]!["inputs"]!;
        inputs["choice"]!.GetValue<string>().Should().Be("Default");
        result.PromptTargetDescription.Should().Contain("preset not among the CustomCombo options");
    }

    [Fact]
    public void Preset_TwoCombos_LeavesBothUntouchedAndNotesIt()
    {
        // Belt-and-braces for a template edited after selection — same invariant as the
        // multi-UNET no-op.
        var result = ComfyUiWorkflowPatcher.Patch(DualComboTemplate, Request(preset: "Quality"));

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["20"]!["inputs"]!["choice"]!.GetValue<string>().Should().Be("Default");
        graph["21"]!["inputs"]!["choice"]!.GetValue<string>().Should().Be("euler");
        result.PromptTargetDescription.Should().Contain("no unambiguous CustomCombo");
    }

    [Fact]
    public void Preset_LinkDrivenIndex_PatchesChoiceButPreservesTheLink()
    {
        const string linkIndex =
            """
            {
              "20": { "class_type": "CustomCombo",
                      "inputs": { "choice": "Default", "index": ["9", 0],
                                  "option1": "Quality", "option2": "Default", "option3": "Turbo", "option4": "" } },
              "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old positive" } },
              "3":  { "class_type": "KSampler", "inputs": { "seed": 7 } }
            }
            """;

        var result = ComfyUiWorkflowPatcher.Patch(linkIndex, Request(preset: "Turbo"));

        var inputs = JsonNode.Parse(result.GraphJson)!["20"]!["inputs"]!;
        inputs["choice"]!.GetValue<string>().Should().Be("Turbo");
        inputs["index"]!.GetValueKind().Should().Be(JsonValueKind.Array, "link-driven inputs are never touched");
    }

    // ---- %date% expansion in filename_prefix --------------------------------------------
    // The browser frontend expands %date:FORMAT% at queue time; the SERVER takes the prefix
    // literally and the ':' inside an unexpanded token is path-invalid on Windows (the live
    // WinError 267 failure). Fixed timestamp exercises zero-padding on M/d/h/m/s.

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 9, 7, 5, 4, TimeSpan.Zero);

    private static string PatchPrefix(string prefix)
    {
        var template =
            $$"""
            {
              "6":   { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } },
              "200": { "class_type": "SaveImage",
                       "inputs": { "images": ["162", 0], "filename_prefix": {{JsonSerializer.Serialize(prefix)}} } }
            }
            """;
        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), FixedNow);
        return JsonNode.Parse(result.GraphJson)!["200"]!["inputs"]!["filename_prefix"]!.GetValue<string>();
    }

    [Fact]
    public void FilenamePrefix_DateToken_ExpandsWithInjectedTimestamp()
    {
        // The user's real workflow_MR prefix shape.
        PatchPrefix("%date:yyyy-MM%/Ideogram4_%date:hhmmss%")
            .Should().Be("2026-06/Ideogram4_070504");
    }

    [Fact]
    public void FilenamePrefix_DateToken_AllFormatChars_PadCorrectly()
    {
        PatchPrefix("%date:yyyy yy MM M dd d hh h mm m ss s%")
            .Should().Be("2026 26 06 6 09 9 07 7 05 5 04 4");
    }

    [Fact]
    public void FilenamePrefix_DateToken_HhIsTwentyFourHourPadded()
    {
        var evening = new DateTimeOffset(2026, 6, 10, 21, 14, 49, TimeSpan.Zero);
        var template =
            """
            {
              "6":   { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } },
              "200": { "class_type": "SaveImage",
                       "inputs": { "filename_prefix": "Ideogram4_%date:hhmmss%" } }
            }
            """;
        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), evening);

        // Matches the frozen "Ideogram4_211449" stamp observed in the live export.
        JsonNode.Parse(result.GraphJson)!["200"]!["inputs"]!["filename_prefix"]!
            .GetValue<string>().Should().Be("Ideogram4_211449");
    }

    [Fact]
    public void FilenamePrefix_BareDateToken_UsesDefaultFormat()
    {
        PatchPrefix("img_%date%").Should().Be("img_20260609070504");
    }

    [Fact]
    public void FilenamePrefix_MultipleTokens_AllExpanded()
    {
        PatchPrefix("%date:yyyy%/%date:MM%/%date:dd%").Should().Be("2026/06/09");
    }

    [Fact]
    public void FilenamePrefix_UnknownSpecChars_PassThroughVerbatim()
    {
        PatchPrefix("%date:yyyy_MM~dd%").Should().Be("2026_06~09");
    }

    [Fact]
    public void FilenamePrefix_UnterminatedToken_IsLeftVerbatim()
    {
        PatchPrefix("img_%date:yyyy").Should().Be("img_%date:yyyy");
    }

    [Fact]
    public void FilenamePrefix_ExpandedResult_ContainsNoPercentOrColon()
    {
        // The WinError 267 guard: nothing path-invalid may survive expansion.
        var expanded = PatchPrefix("%date:yyyy-MM%/Ideogram4_%date:hhmmss%");
        expanded.Should().NotContain("%").And.NotContain(":");
    }

    [Fact]
    public void FilenamePrefix_LinkValue_IsLeftUntouched()
    {
        const string template =
            """
            {
              "6":   { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } },
              "200": { "class_type": "SaveImage", "inputs": { "filename_prefix": ["9", 0] } }
            }
            """;
        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), FixedNow);

        JsonNode.Parse(result.GraphJson)!["200"]!["inputs"]!["filename_prefix"]!
            .GetValueKind().Should().Be(JsonValueKind.Array);
        result.PromptTargetDescription.Should().NotContain("%date%");
    }

    [Fact]
    public void FilenamePrefix_WithoutToken_IsUnchangedAndNoNoteEmitted()
    {
        var result = ComfyUiWorkflowPatcher.Patch(IdeogramTemplate, Request(json: true), FixedNow);

        JsonNode.Parse(result.GraphJson)!["200"]!["inputs"]!["filename_prefix"]!
            .GetValue<string>().Should().Be("Ideogram4");
        result.PromptTargetDescription.Should().NotContain("%date%");
    }

    [Fact]
    public void DateExpansion_AppliesToEveryNodeWithFilenamePrefix_AndNotesTheCount()
    {
        const string template =
            """
            {
              "6":   { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } },
              "200": { "class_type": "SaveImage", "inputs": { "filename_prefix": "%date:yyyy%/a" } },
              "201": { "class_type": "SaveImage", "inputs": { "filename_prefix": "%date:yyyy%/b" } }
            }
            """;
        var result = ComfyUiWorkflowPatcher.Patch(template, Request(), FixedNow);

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["200"]!["inputs"]!["filename_prefix"]!.GetValue<string>().Should().Be("2026/a");
        graph["201"]!["inputs"]!["filename_prefix"]!.GetValue<string>().Should().Be("2026/b");
        result.PromptTargetDescription.Should().Contain("%date% expanded on 2 filename_prefix input(s)");
    }
}
