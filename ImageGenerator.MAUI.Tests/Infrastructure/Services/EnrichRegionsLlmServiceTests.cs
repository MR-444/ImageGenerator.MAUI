using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Infrastructure.External.Mutation;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

/// <summary>
/// The region-enricher's seam: a fake <see cref="EnrichRegionsLlmService.EnrichCompletion"/> replaces the
/// network so the facts-block build, tier routing, validate-retry loop, override precedence, and the
/// desc-splice preservation (preserved fields come from the original, only descs from the model) run
/// without disk or HTTP.
/// </summary>
public sealed class EnrichRegionsLlmServiceTests : IDisposable
{
    // Two placed elements with bboxes + descs — gives the fact block content and the desc-splice
    // something to map.
    private const string BaseJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"a russet fox mid-step"},{"type":"text","bbox":[100,100,200,500],"text":"WINTER","desc":"title lettering"}]}}""";

    // Same scene, the descs rewritten → the spliced result passes the validator.
    private const string EnrichedJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"a russet fox mid-step in the lower-left, set against the snowfield"},{"type":"text","bbox":[100,100,200,500],"text":"WINTER","desc":"title lettering across the upper sky band"}]}}""";

    // Aligned (same count + types), but MOVES a bbox → the bbox is spliced from the base, so the move is ignored.
    private const string MovedBboxJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[401,0,800,400],"desc":"a russet fox mid-step"},{"type":"text","bbox":[100,100,200,500],"text":"WINTER","desc":"title lettering"}]}}""";

    // Aligned, but CHANGES the literal text → text is spliced from the base, so the change is ignored.
    private const string ChangedTextJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"a russet fox mid-step"},{"type":"text","bbox":[100,100,200,500],"text":"SUMMER","desc":"title lettering"}]}}""";

    // DROPS an element → count mismatch the splice can't map 1:1.
    private const string DroppedElementJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"a russet fox mid-step"}]}}""";

    // RETYPES element #1 (text → obj) → type mismatch; a desc would map onto the wrong subject.
    private const string RetypedElementJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"a russet fox mid-step"},{"type":"obj","bbox":[100,100,200,500],"desc":"title lettering"}]}}""";

    // Aligned, but BLANKS a desc → the spliced result fails the validator (desc is the model's to set).
    private const string ValidationFailJson =
        """{"high_level_description":"A red fox in fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":""},{"type":"text","bbox":[100,100,200,500],"text":"WINTER","desc":"title lettering"}]}}""";

    // The real-world bug: a web-pasted prompt whose headline AND style carry a curly apostrophe (U+2019)
    // in "worm’s-eye". The model returns the same scene with both straightened to ASCII — which the old
    // exact-match gate rejected, but the splice keeps the original bytes.
    private const string Curly = "’";

    private static readonly string CurlyBaseJson =
        "{\"high_level_description\":\"A worm" + Curly + "s-eye view of a red fox\"," +
        "\"style_description\":{\"medium\":\"photographic film\",\"photo\":\"worm" + Curly + "s-eye cinematic shot\"}," +
        "\"compositional_deconstruction\":{\"background\":\"a quiet snowy field\",\"elements\":[" +
        "{\"type\":\"obj\",\"bbox\":[400,0,800,400],\"desc\":\"a russet fox mid-step\"}]}}";

    private const string CurlyStraightenedReply =
        """{"high_level_description":"A worm's-eye view of a red fox","style_description":{"medium":"photographic film","photo":"worm's-eye cinematic shot"},"compositional_deconstruction":{"background":"a quiet snowy field","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"a russet fox mid-step in the lower-left against the snowfield"}]}}""";

    private static readonly V4JsonPrompt Base = V4JsonPromptSerializer.Deserialize(BaseJson);

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "enrich-llm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    // ---- Happy path + structured call ----------------------------------------------------

    [Fact]
    public async Task EnrichAsync_DescOnlyChange_SucceedsFromASchemaConstrainedCall()
    {
        var schemaKind = JsonValueKind.Undefined;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, schema, _) => { schemaKind = schema.ValueKind; return Task.FromResult(EnrichedJson); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeTrue();
        result.Prompt!.CompositionalDeconstruction.Elements[0].Desc.Should().Contain("lower-left");
        result.Label.Should().StartWith("Enriched:");
        schemaKind.Should().Be(JsonValueKind.Object, "every enrichment call is constrained to the V4 schema");
    }

    // ---- Model routing -------------------------------------------------------------------

    [Theory]
    [InlineData(ModelTier.Sonnet, "claude-sonnet-4-6")]
    [InlineData(ModelTier.Opus, "claude-opus-4-8")]
    public async Task EnrichAsync_AnthropicTiers_PassTheExpectedModelId(ModelTier tier, string expectedModel)
    {
        string? seenModel = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, modelId, _, _, _, _, _, _) => { seenModel = modelId; return Task.FromResult(EnrichedJson); });

        await sut.EnrichAsync(Base, tier);

        seenModel.Should().Be(expectedModel);
    }

    [Fact]
    public async Task EnrichAsync_LocalTier_UsesOllamaModelAndBaseUrl_AndNeedsNoKey()
    {
        var uiStore = new Mock<IUiStateStore>();
        uiStore.Setup(s => s.LoadOllamaModel()).Returns("qwen3");
        uiStore.Setup(s => s.LoadOllamaBaseUrl()).Returns("http://host:11434");

        ModelTier seenTier = ModelTier.Sonnet;
        string? seenModel = null, seenBaseUrl = null, seenKey = "unset";
        var sut = CreateSut(KeyStore(null),
            (tier, modelId, apiKey, baseUrl, _, _, _, _) =>
            {
                seenTier = tier; seenModel = modelId; seenBaseUrl = baseUrl; seenKey = apiKey;
                return Task.FromResult(EnrichedJson);
            },
            uiStore);

        var result = await sut.EnrichAsync(Base, ModelTier.Local);

        result.Success.Should().BeTrue("the Local tier needs no Anthropic key");
        seenTier.Should().Be(ModelTier.Local);
        seenModel.Should().Be("qwen3");
        seenBaseUrl.Should().Be("http://host:11434");
        seenKey.Should().BeNull();
    }

    [Fact]
    public async Task EnrichAsync_AnthropicTier_NoApiKey_FailsWithoutCallingTheModel()
    {
        var called = false;
        var sut = CreateSut(KeyStore(null),
            (_, _, _, _, _, _, _, _) => { called = true; return Task.FromResult(EnrichedJson); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
        called.Should().BeFalse();
    }

    // ---- Validate + retry ----------------------------------------------------------------

    [Fact]
    public async Task EnrichAsync_InvalidThenValid_RetriesWithFeedbackAndSucceeds()
    {
        var seen = new List<IReadOnlyList<ImageGenerator.MAUI.Infrastructure.External.Anthropic.ChatTurn>>();
        var call = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, messages, _, _) => { seen.Add(messages); return Task.FromResult(call++ == 0 ? ValidationFailJson : EnrichedJson); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeTrue();
        seen.Should().HaveCount(2);
        seen[1].Should().Contain(t => t.Role == "assistant");
        seen[1].Should().Contain(t => t.Role == "user" && t.Content.Contains("ONLY the element descriptions"));
    }

    [Fact]
    public async Task EnrichAsync_ValidationFailsTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult(ValidationFailJson); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task EnrichAsync_UnparseableTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult("not json {"); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("structured prompt");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task EnrichAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach the model");
    }

    // ---- Desc-splice preservation (only descs are taken from the model) -------------------

    [Theory]
    [InlineData(nameof(MovedBboxJson))]
    [InlineData(nameof(ChangedTextJson))]
    public async Task EnrichAsync_ModelTouchesPreservedField_SilentlyCorrectedFromBase(string which)
    {
        var reply = which switch
        {
            nameof(MovedBboxJson) => MovedBboxJson,
            _ => ChangedTextJson
        };
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult(reply); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeTrue("preserved fields are spliced from the original, never validated away");
        calls.Should().Be(1, "no retry needed — the model's preserved-field edit is simply ignored");
        // bbox + literal text come from the base regardless of what the model returned.
        result.Prompt!.CompositionalDeconstruction.Elements[0].Bbox.Should().Equal(400, 0, 800, 400);
        result.Prompt.CompositionalDeconstruction.Elements[1].Text.Should().Be("WINTER");
    }

    [Fact]
    public async Task EnrichAsync_ModelStraightensCurlyApostrophe_PreservesOriginalBytesExactly()
    {
        // The reported bug: U+2019 in the headline and style; the model straightens both. Splicing
        // keeps the user's original bytes, so the enrichment succeeds instead of failing the old gate.
        var baseCaption = V4JsonPromptSerializer.Deserialize(CurlyBaseJson);
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult(CurlyStraightenedReply); });

        var result = await sut.EnrichAsync(baseCaption, ModelTier.Sonnet);

        result.Success.Should().BeTrue("preserved fields come from the original, so a straightened quote can't fail it");
        calls.Should().Be(1, "preserved fields are never compared against the model output");
        result.Prompt!.HighLevelDescription.Should().Be(baseCaption.HighLevelDescription)
            .And.Contain(Curly, "the curly apostrophe survives byte-for-byte");
        result.Prompt.StyleDescription!.Photo.Should().Be(baseCaption.StyleDescription!.Photo)
            .And.Contain(Curly);
        // The element desc, by contrast, IS the model's rewrite.
        result.Prompt.CompositionalDeconstruction.Elements[0].Desc.Should().Contain("lower-left");
    }

    [Theory]
    [InlineData(nameof(DroppedElementJson))]
    [InlineData(nameof(RetypedElementJson))]
    public async Task EnrichAsync_ElementListMisaligned_RetriesThenFails(string which)
    {
        var bad = which switch
        {
            nameof(DroppedElementJson) => DroppedElementJson,
            _ => RetypedElementJson
        };
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult(bad); });

        var result = await sut.EnrichAsync(Base, ModelTier.Sonnet);

        result.Success.Should().BeFalse("a misaligned element list can't be spliced 1:1");
        calls.Should().Be(2, "the misalignment triggers the one feedback retry");
    }

    // ---- Facts block ---------------------------------------------------------------------

    [Fact]
    public void BuildEnrichUserTurn_EmitsSoftDepthCue_ButNeverAssertsFrontOrBehind()
    {
        // Two overlapping boxes so a DEPTH CUE line is actually produced.
        var overlapping = V4JsonPromptSerializer.Deserialize(
            """{"high_level_description":"h","compositional_deconstruction":{"background":"b","elements":[{"type":"obj","bbox":[400,0,1000,600],"desc":"big near subject"},{"type":"obj","bbox":[200,300,600,700],"desc":"smaller subject"}]}}""");

        var turn = EnrichRegionsLlmService.BuildEnrichUserTurn(overlapping, RegionGraph.Compute(overlapping));

        turn.Should().Contain("SPATIAL FACTS").And.Contain("[#0]").And.Contain("[#1]");
        turn.Should().Contain("DEPTH CUE").And.Contain("leans nearer").And.Contain("SOFT");
        // Geometry states a SOFT cue, never a hard occlusion verdict.
        turn.Should().NotContainEquivalentOf("in front of");
        turn.Should().NotContainEquivalentOf("is behind");
    }

    [Fact]
    public void BuildEnrichUserTurn_FlagsUnplacedElements()
    {
        var withUnplaced = V4JsonPromptSerializer.Deserialize(
            """{"high_level_description":"h","compositional_deconstruction":{"background":"b","elements":[{"type":"obj","bbox":[400,0,800,400],"desc":"placed"},{"type":"text","text":"OPEN","desc":"floating"}]}}""");

        var turn = EnrichRegionsLlmService.BuildEnrichUserTurn(withUnplaced, RegionGraph.Compute(withUnplaced));

        turn.Should().Contain("unplaced");
    }

    [Fact]
    public void BuildEnrichUserTurn_FormatsNumbersInvariant_UnderCommaDecimalCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var turn = EnrichRegionsLlmService.BuildEnrichUserTurn(Base, RegionGraph.Compute(Base));

            // fox center x = 200/1000 = 0.20 → must render with a '.' decimal, never the de-DE comma.
            turn.Should().Contain("x0.20");
            turn.Should().NotContain("x0,20");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---- System-prompt override precedence -----------------------------------------------

    [Fact]
    public async Task EnrichAsync_OverridePresent_UsesItInsteadOfBundled()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "enrich-region-prompt.md"), "PRIVATE ENRICH OVERRIDE");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, system, _, _, _) => { captured = system; return Task.FromResult(EnrichedJson); });

        await sut.EnrichAsync(Base, ModelTier.Sonnet);

        captured.Should().Be("PRIVATE ENRICH OVERRIDE");
    }

    [Fact]
    public async Task EnrichAsync_NoOverride_UsesBundledAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, system, _, _, _) => { captured = system; return Task.FromResult(EnrichedJson); });

        await sut.EnrichAsync(Base, ModelTier.Sonnet);

        captured.Should().Be("BUNDLED ENRICH PROMPT");
    }

    [Fact]
    public void ShippedEnrichSystemPrompt_ExistsAndIsNonEmpty()
    {
        var path = ShippedPromptPath();
        File.Exists(path).Should().BeTrue($"the bundled clean-room enrichment prompt should ship at {path}");
        File.ReadAllText(path).Trim().Should().NotBeEmpty();
    }

    // ---- Helpers / fakes -----------------------------------------------------------------

    private EnrichRegionsLlmService CreateSut(
        Mock<IAnthropicTokenStore> tokenStore,
        EnrichRegionsLlmService.EnrichCompletion completion,
        Mock<IUiStateStore>? uiStore = null) =>
        new(tokenStore.Object,
            (uiStore ?? new Mock<IUiStateStore>()).Object,
            NullLogger<EnrichRegionsLlmService>.Instance,
            completion,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    private static Mock<IAnthropicTokenStore> KeyStore(string? key)
    {
        var mock = new Mock<IAnthropicTokenStore>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock;
    }

    private static Task<Stream> FakeBundledAsset(string assetName) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("BUNDLED ENRICH PROMPT")));

    private static string ShippedPromptPath([CallerFilePath] string? thisFile = null)
    {
        var servicesDir = Path.GetDirectoryName(thisFile)!;                 // ...\Tests\Infrastructure\Services
        var repoRoot = Path.GetFullPath(Path.Combine(servicesDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "ImageGenerator.MAUI", "Resources", "Raw", "Mutation", "enrich-region-system.md");
    }
}
