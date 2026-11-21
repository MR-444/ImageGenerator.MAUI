using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class IdeogramStructureEditorViewModelTests
{
    private const string SampleJson =
        "{\"high_level_description\":\"A poster\"," +
        "\"style_description\":{\"medium\":\"poster\",\"art_style\":\"art deco\",\"color_palette\":[\"#112233\"]}," +
        "\"compositional_deconstruction\":{\"background\":\"navy sky\",\"elements\":[" +
        "{\"type\":\"obj\",\"bbox\":[100,200,300,400],\"desc\":\"a lighthouse\"}," +
        "{\"type\":\"text\",\"bbox\":[0,0,100,1000],\"text\":\"BEACON\",\"desc\":\"headline\",\"color_palette\":[\"#FFFFFF\"]}]}}";

    private readonly Mock<IJsonPromptFileService> _fileService = new();
    private readonly Mock<IFileLauncher> _fileLauncher = new();

    private IdeogramStructureEditorViewModel CreateSut() =>
        new(_fileService.Object, _fileLauncher.Object, NullLogger<IdeogramStructureEditorViewModel>.Instance);

    private static IdeogramStructureEditorViewModel FillValid(IdeogramStructureEditorViewModel sut)
    {
        sut.HighLevelDescription = "A poster";
        sut.Background = "navy sky";
        return sut;
    }

    [Fact]
    public void Editor_WithoutGenerator_UsesIdeogramResolutionMode()
    {
        // Stand-alone construction (tests, future hosts) must keep the classic behavior:
        // Ideogram's resolution list, "Target resolution" title, Auto default.
        var sut = CreateSut();

        sut.IsAspectRatioMode.Should().BeFalse();
        sut.ResolutionPickerTitle.Should().Be("Target resolution");
        sut.ResolutionOptions.Should().Contain("Auto").And.Contain("1440x2880");
        sut.SelectedResolution.Should().Be("Auto");
    }



    [Fact]
    public void LoadFromJson_ValidStructuredPrompt_PopulatesAllFields()
    {
        var sut = CreateSut();

        sut.LoadFromJson(SampleJson);

        sut.HighLevelDescription.Should().Be("A poster");
        sut.Background.Should().Be("navy sky");
        sut.IncludeStyle.Should().BeTrue();
        sut.Medium.Should().Be("poster");
        sut.ArtStyle.Should().Be("art deco");
        sut.IsPhoto.Should().BeFalse();
        sut.StylePaletteText.Should().Be("#112233");
        sut.Elements.Should().HaveCount(2);
        sut.Elements[0].IsText.Should().BeFalse();
        sut.Elements[1].Text.Should().Be("BEACON");
        sut.Elements[1].HasBbox.Should().BeTrue();
        sut.Elements[1].XMax.Should().Be(1000);
        sut.SelectedElement.Should().BeSameAs(sut.Elements[0]);
    }

    [Fact]
    public void LoadFromJson_RoundTrip_ReproducesTheExactCompactString()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);

        var rebuilt = V4JsonPromptSerializer.Serialize(sut.BuildModel());

        rebuilt.Should().Be(SampleJson);
    }

    [Fact]
    public void JsonPreview_PrettyPrintsAndTracksElementEdits()
    {
        var sut = FillValid(CreateSut());
        sut.AddObjElementCommand.Execute(null);

        sut.JsonPreview.Should().Contain(Environment.NewLine);
        sut.JsonPreview.Should().Contain("\"high_level_description\": \"A poster\"");

        sut.SelectedElement!.Desc = "a bright lighthouse";

        sut.JsonPreview.Should().Contain("a bright lighthouse");
    }

    [Fact]
    public void LoadFromJson_Garbage_StartsFreshWithWarning_WithoutThrowing()
    {
        var sut = CreateSut();

        sut.LoadFromJson("a plain text prompt, not json");

        sut.Elements.Should().BeEmpty();
        sut.HighLevelDescription.Should().BeEmpty();
        sut.StatusKind.Should().Be(StatusKind.Warning);
    }

    [Fact]
    public void LoadFromJson_Empty_StartsFreshWithInfoNote()
    {
        var sut = CreateSut();

        sut.LoadFromJson(null);

        sut.Elements.Should().BeEmpty();
        sut.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public void AddObjAndTextElements_AddAndSelect_WithCorrectTypes()
    {
        var sut = CreateSut();

        sut.AddObjElementCommand.Execute(null);
        sut.AddTextElementCommand.Execute(null);

        sut.Elements.Should().HaveCount(2);
        sut.Elements[0].IsText.Should().BeFalse();
        sut.Elements[1].IsText.Should().BeTrue();
        sut.SelectedElement.Should().BeSameAs(sut.Elements[1]);
        sut.Elements[1].HasBbox.Should().BeTrue();
    }

    [Fact]
    public void DeleteSelectedElement_RemovesAndReselectsNeighbor()
    {
        var sut = CreateSut();
        sut.AddObjElementCommand.Execute(null);
        sut.AddTextElementCommand.Execute(null);
        sut.SelectedElement = sut.Elements[0];

        sut.DeleteSelectedElementCommand.Execute(null);

        sut.Elements.Should().ContainSingle();
        sut.SelectedElement.Should().BeSameAs(sut.Elements[0]);
    }

    [Fact]
    public async Task Apply_WithValidationErrors_SetsErrorStatus_AndDoesNotNavigate()
    {
        var sut = CreateSut(); // everything blank -> required-field errors

        await sut.ApplyCommand.ExecuteAsync(null);

        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.StatusMessage.Should().Contain("required");
        // Navigation failure (no Shell in tests) reports a different message; validation
        // errors must short-circuit before any navigation is attempted.
        sut.StatusMessage.Should().NotContain("Couldn't return");
    }

    [Fact]
    public async Task SaveToFile_ValidModel_WritesPrettyJson_AndExposesPath()
    {
        _fileService
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"C:\fake\out.json");
        var sut = FillValid(CreateSut());

        await sut.SaveToFileCommand.ExecuteAsync(null);

        sut.ExportedPath.Should().Be(@"C:\fake\out.json");
        sut.HasExportedPath.Should().BeTrue();
        sut.StatusKind.Should().Be(StatusKind.Success);
        _fileService.Verify(s => s.SaveAsync(
            "A poster",
            It.Is<string>(json => json.Contains(Environment.NewLine)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveToFile_InvalidModel_DoesNotTouchDisk()
    {
        var sut = CreateSut();

        await sut.SaveToFileCommand.ExecuteAsync(null);

        sut.StatusKind.Should().Be(StatusKind.Error);
        _fileService.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveToFile_ServiceThrows_ReportsErrorStatus()
    {
        _fileService
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));
        var sut = FillValid(CreateSut());

        await sut.SaveToFileCommand.ExecuteAsync(null);

        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.StatusMessage.Should().Contain("disk full");
    }

    [Fact]
    public void ShowExportInFolder_RevealsTheExportedFile()
    {
        var sut = CreateSut();
        sut.ExportedPath = @"C:\fake\out.json";

        sut.ShowExportInFolderCommand.Execute(null);

        _fileLauncher.Verify(l => l.RevealInFolder(@"C:\fake\out.json"), Times.Once);
    }

    [Fact]
    public void CanvasPointerPressed_SelectsTopmostHit_AndIgnoresMisses()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson); // [0] obj y100-300/x200-400, [1] text y0-100/x0-1000; both overlap at y=100,x=300
        sut.SelectedElement = null;

        // 480px square canvas: pixel 96 -> grid 200. Point (grid 300, 50) hits only the text strip.
        sut.CanvasPointerPressed(pixelX: 144, pixelY: 24, canvasWidth: 480, canvasHeight: 480);
        sut.SelectedElement.Should().BeSameAs(sut.Elements[1]);
        sut.CanvasPointerReleased();

        // Point (grid 300, 200) hits only the obj box.
        sut.CanvasPointerPressed(pixelX: 144, pixelY: 96, canvasWidth: 480, canvasHeight: 480);
        sut.SelectedElement.Should().BeSameAs(sut.Elements[0]);
        sut.CanvasPointerReleased();

        // A miss keeps the current selection.
        sut.CanvasPointerPressed(pixelX: 470, pixelY: 470, canvasWidth: 480, canvasHeight: 480);
        sut.SelectedElement.Should().BeSameAs(sut.Elements[0]);
        sut.CanvasPointerReleased();

        // Non-square canvas (portrait 240x480): same grid point (300, 200) is pixel (72, 96).
        sut.SelectedElement = null;
        sut.CanvasPointerPressed(pixelX: 72, pixelY: 96, canvasWidth: 240, canvasHeight: 480);
        sut.SelectedElement.Should().BeSameAs(sut.Elements[0]);
    }

    // --- Canvas drag/resize gestures (Phase 2) --------------------------------------------
    // All on a 480x480 canvas: pixel = grid * 0.48. The obj box from SampleJson is
    // y100-300/x200-400 (a 200x200 box).

    [Fact]
    public void CanvasDrag_InsideBox_MovesIt_PreservingSize()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);

        // Grab the obj box at grid (x300, y200) — offset (100, 100) from its min corner.
        sut.CanvasPointerPressed(144, 96, 480, 480);
        // Drag the pointer to grid (x500, y400).
        sut.CanvasPointerDragged(240, 192, 480, 480);

        var box = sut.Elements[0];
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((400, 600, 300, 500));
    }

    [Fact]
    public void CanvasDrag_Move_ClampsAtCanvasEdge_PreservingSize()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);

        sut.CanvasPointerPressed(144, 96, 480, 480);
        // Pointer to the far bottom-right corner: box pins at the edge, stays 200x200.
        sut.CanvasPointerDragged(480, 480, 480, 480);

        var box = sut.Elements[0];
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((800, 1000, 800, 1000));
    }

    [Fact]
    public void CanvasDrag_BottomRightHandle_ResizesOnlyMaxPair()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);
        sut.SelectedElement = sut.Elements[0];

        // The obj box's bottom-right corner grid (x400, y300) sits at pixel (192, 144).
        sut.CanvasPointerPressed(192, 144, 480, 480);
        // Drag to grid (x500, y500).
        sut.CanvasPointerDragged(240, 240, 480, 480);

        var box = sut.Elements[0];
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((200, 500, 100, 500));
    }

    [Fact]
    public void CanvasDrag_TopLeftHandle_ClampsToGridZero()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);
        sut.SelectedElement = sut.Elements[0];

        // TL corner grid (x200, y100) = pixel (96, 48); drag off-canvas.
        sut.CanvasPointerPressed(96, 48, 480, 480);
        sut.CanvasPointerDragged(-10, -10, 480, 480);

        var box = sut.Elements[0];
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((0, 400, 0, 300));
    }

    [Fact]
    public void CanvasDrag_Resize_EnforcesMinimumBoxSize()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);
        sut.SelectedElement = sut.Elements[0];

        // Grab the BR handle and drag PAST the box's own min corner: edges stop at MinBoxSize.
        sut.CanvasPointerPressed(192, 144, 480, 480);
        sut.CanvasPointerDragged(96, 48, 480, 480); // pointer at grid (x200, y100) = the TL corner

        var box = sut.Elements[0];
        (box.XMin, box.XMax, box.YMin, box.YMax).Should()
            .Be((200, 200 + IdeogramStructureEditorViewModel.MinBoxSize,
                 100, 100 + IdeogramStructureEditorViewModel.MinBoxSize));
    }

    [Fact]
    public void CanvasPress_HandleOfSelectedBox_WinsOverOverlappingBody()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);
        sut.SelectedElement = sut.Elements[0];

        // The obj box's TL corner (grid x200, y100 = pixel 96, 48) lies ON the text strip
        // (y0-100/x0-1000). The selected box's handle must win: no re-selection, and the
        // following drag resizes the obj box instead of moving the strip.
        sut.CanvasPointerPressed(96, 48, 480, 480);
        sut.SelectedElement.Should().BeSameAs(sut.Elements[0]);

        sut.CanvasPointerDragged(48, 24, 480, 480); // grid (x100, y50)

        var box = sut.Elements[0];
        (box.XMin, box.YMin).Should().Be((100, 50));
        var strip = sut.Elements[1];
        (strip.YMin, strip.XMin, strip.YMax, strip.XMax).Should().Be((0, 0, 100, 1000));
    }

    [Fact]
    public void CanvasDrag_WithoutPress_OrAfterRelease_IsANoOp()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);

        // No press at all.
        sut.CanvasPointerDragged(240, 240, 480, 480);
        var box = sut.Elements[0];
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((200, 400, 100, 300));

        // Press on empty canvas: still no gesture.
        sut.CanvasPointerPressed(470, 470, 480, 480);
        sut.CanvasPointerDragged(240, 240, 480, 480);
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((200, 400, 100, 300));

        // Genuine move, then release: a stale drag afterwards must change nothing.
        sut.CanvasPointerPressed(144, 96, 480, 480);
        sut.CanvasPointerReleased();
        sut.CanvasPointerDragged(480, 480, 480, 480);
        (box.XMin, box.XMax, box.YMin, box.YMax).Should().Be((200, 400, 100, 300));
    }

    [Fact]
    public void CanvasPress_IgnoresElementsWithoutBbox()
    {
        var sut = CreateSut();
        sut.LoadFromJson(SampleJson);
        sut.SelectedElement = null;
        sut.Elements[1].HasBbox = false;

        // Inside where the text strip used to be: nothing selectable there any more.
        sut.CanvasPointerPressed(144, 24, 480, 480);

        sut.SelectedElement.Should().BeNull();
    }

    [Fact]
    public void CanvasInvalidated_FiresOnAddSelectAndItemEdits()
    {
        var sut = CreateSut();
        var fired = 0;
        sut.CanvasInvalidated += () => fired++;

        sut.AddObjElementCommand.Execute(null);
        var afterAdd = fired;
        sut.Elements[0].YMin = 10;

        afterAdd.Should().BeGreaterThan(0);
        fired.Should().BeGreaterThan(afterAdd);
    }

    [Fact]
    public void BuildModel_PhotoMode_EmitsPhotoAndDropsArtStyle()
    {
        var sut = FillValid(CreateSut());
        sut.IncludeStyle = true;
        sut.Medium = "photograph";
        sut.ArtStyle = "stale value";
        sut.IsPhoto = true;
        sut.PhotoStyle = "35mm film";

        var style = sut.BuildModel().StyleDescription!;

        style.Photo.Should().Be("35mm film");
        style.ArtStyle.Should().BeNull();
    }

    [Fact]
    public void BuildModel_WithoutIncludeStyle_OmitsStyleDescription()
    {
        var sut = FillValid(CreateSut());
        sut.Medium = "stale";

        sut.BuildModel().StyleDescription.Should().BeNull();
    }

    [Fact]
    public void ElementItem_BboxValues_ClampOntoGrid()
    {
        var item = new ElementItemViewModel(Element.ObjType)
        {
            YMin = -50,
            XMax = 2000
        };

        item.YMin.Should().Be(0);
        item.XMax.Should().Be(1000);
    }

    [Fact]
    public void ElementItem_ToElement_ObjIgnoresText_AndUnplacedHasNoBbox()
    {
        var item = new ElementItemViewModel(Element.ObjType)
        {
            Desc = " a lighthouse ",
            Text = "stale",
            HasBbox = false
        };

        var element = item.ToElement();

        element.Text.Should().BeNull();
        element.Desc.Should().Be("a lighthouse");
        element.Bbox.Should().BeNull();
    }

    [Fact]
    public void ParsePalette_NormalizesSeparatorsCaseAndMissingHash()
    {
        ElementItemViewModel.ParsePalette("ff0000, #00ff00;  #0000FF")
            .Should().Equal("#FF0000", "#00FF00", "#0000FF");
    }

    [Fact]
    public void ParsePalette_Blank_ReturnsNull()
    {
        ElementItemViewModel.ParsePalette("   ").Should().BeNull();
    }

    [Theory]
    [InlineData("1440x2880", 240, 480)]  // portrait: height pinned to the fit box
    [InlineData("2880x1440", 480, 240)]  // landscape: width pinned
    [InlineData("2048x2048", 480, 480)]  // square
    [InlineData("Auto", 480, 480)]       // sentinel -> square preview
    public void SelectedResolution_ShapesCanvasToAspectRatio(string resolution, double width, double height)
    {
        var sut = CreateSut();

        sut.SelectedResolution = resolution;

        sut.CanvasWidthRequest.Should().BeApproximately(width, 0.001);
        sut.CanvasHeightRequest.Should().BeApproximately(height, 0.001);
    }

    [Fact]
    public void SetCanvasFitBox_ReshapesCurrentCanvasWithinStageBounds()
    {
        var sut = CreateSut();
        sut.SelectedResolution = "1440x2880";

        sut.SetCanvasFitBox(640);

        sut.CanvasWidthRequest.Should().BeApproximately(320, 0.001);
        sut.CanvasHeightRequest.Should().BeApproximately(640, 0.001);
    }

    [Fact]
    public void SetIncomingResolution_UnknownValue_FallsBackToAuto()
    {
        var sut = CreateSut();
        sut.SelectedResolution = "1440x2880";

        sut.SetIncomingResolution("999x999");

        sut.SelectedResolution.Should().Be("Auto");
        sut.CanvasWidthRequest.Should().Be(IdeogramStructureEditorViewModel.CanvasFitBox);
    }

    [Fact]
    public void SetIncomingResolution_KnownValue_IsAdopted()
    {
        var sut = CreateSut();

        sut.SetIncomingResolution("1440x2880");

        sut.SelectedResolution.Should().Be("1440x2880");
    }

    [Fact]
    public void SelectedResolution_Change_InvalidatesCanvas()
    {
        var sut = CreateSut();
        var fired = 0;
        sut.CanvasInvalidated += () => fired++;

        sut.SelectedResolution = "1440x2880";

        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PickerHexAndPreview_FollowTheRgbSliders()
    {
        var sut = CreateSut();

        sut.PickerRed = 17;
        sut.PickerGreen = 34;
        sut.PickerBlue = 51;

        sut.PickerHex.Should().Be("#112233");
        sut.PickerPreviewColor.ToArgbHex().Should().Be("#112233");
    }

    [Fact]
    public void AddPickerColorToStyle_AppendsHex_AndForcesStyleOn()
    {
        var sut = CreateSut();
        sut.StylePaletteText = "#AABBCC";
        sut.PickerRed = 255; sut.PickerGreen = 0; sut.PickerBlue = 0;

        sut.AddPickerColorToStyleCommand.Execute(null);

        sut.StylePaletteText.Should().Be("#AABBCC, #FF0000");
        sut.StylePaletteSwatches.Should().HaveCount(2);
        sut.IncludeStyle.Should().BeTrue("a palette only ships inside style_description");
    }

    [Fact]
    public void AddPickerColorToStyle_AtCap_RefusesWithWarning()
    {
        var sut = CreateSut();
        sut.StylePaletteText = string.Join(", ", Enumerable.Repeat("#112233", 16));

        sut.AddPickerColorToStyleCommand.Execute(null);

        sut.StylePaletteSwatches.Should().HaveCount(16);
        sut.StatusKind.Should().Be(StatusKind.Warning);
        sut.StatusMessage.Should().Contain("16");
    }

    [Fact]
    public void AddPickerColorToElement_AppendsToSelectedElement_AndRespectsCap()
    {
        var sut = CreateSut();
        sut.AddObjElementCommand.Execute(null);
        sut.PickerRed = 0; sut.PickerGreen = 255; sut.PickerBlue = 0;

        sut.AddPickerColorToElementCommand.Execute(null);
        sut.SelectedElement!.PaletteText.Should().Be("#00FF00");

        sut.SelectedElement.PaletteText = string.Join(", ", Enumerable.Repeat("#112233", 5));
        sut.AddPickerColorToElementCommand.Execute(null);

        sut.SelectedElement.Swatches.Should().HaveCount(5);
        sut.StatusKind.Should().Be(StatusKind.Warning);
    }

    [Fact]
    public void AddPickerColorToStyle_IgnoresDuplicate_AndWarns()
    {
        var sut = CreateSut();
        sut.StylePaletteText = "#FF0000";
        sut.PickerRed = 255; sut.PickerGreen = 0; sut.PickerBlue = 0;

        sut.AddPickerColorToStyleCommand.Execute(null);

        sut.StylePaletteText.Should().Be("#FF0000");
        sut.StatusKind.Should().Be(StatusKind.Warning);
        sut.StatusMessage.Should().Contain("#FF0000");
    }

    [Fact]
    public void AddPickerColorToElement_IgnoresDuplicate_AndWarns()
    {
        var sut = CreateSut();
        sut.AddObjElementCommand.Execute(null);
        sut.PickerRed = 0; sut.PickerGreen = 255; sut.PickerBlue = 0;

        sut.AddPickerColorToElementCommand.Execute(null);
        sut.AddPickerColorToElementCommand.Execute(null);

        sut.SelectedElement!.PaletteText.Should().Be("#00FF00");
        sut.StatusKind.Should().Be(StatusKind.Warning);
        sut.StatusMessage.Should().Contain("#00FF00");
    }

    [Fact]
    public void RemoveStyleColor_RebuildsPaletteText()
    {
        var sut = CreateSut();
        sut.StylePaletteText = "#AABBCC, #112233";

        sut.RemoveStyleColorCommand.Execute("#AABBCC");

        sut.StylePaletteText.Should().Be("#112233");
        sut.StylePaletteSwatches.Should().ContainSingle();
    }

    [Fact]
    public void RemoveElementColor_EditsTheSelectedElement()
    {
        var sut = CreateSut();
        sut.AddTextElementCommand.Execute(null);
        sut.SelectedElement!.PaletteText = "#AABBCC, #112233";

        sut.RemoveElementColorCommand.Execute("#112233");

        sut.SelectedElement.PaletteText.Should().Be("#AABBCC");
    }

    [Fact]
    public void ElementSwatches_RecomputeWhenPaletteTextChanges()
    {
        var item = new ElementItemViewModel(Element.ObjType);
        var notified = false;
        item.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(item.Swatches);

        item.PaletteText = "#AABBCC";

        notified.Should().BeTrue();
        item.Swatches.Should().ContainSingle().Which.Hex.Should().Be("#AABBCC");
    }

    [Fact]
    public void StylePaletteText_NormalizesLowercaseHexToUppercase()
    {
        var sut = CreateSut();

        sut.StylePaletteText = "#aabbcc, #ff00ee";

        sut.StylePaletteText.Should().Be("#AABBCC, #FF00EE");
        sut.StylePaletteSwatches.Should().HaveCount(2)
            .And.OnlyContain(s => s.Hex == s.Hex.ToUpperInvariant());
    }

    [Fact]
    public void ElementPaletteText_NormalizesLowercaseHexToUppercase()
    {
        var item = new ElementItemViewModel(Element.ObjType);

        item.PaletteText = "#aabbcc";

        item.PaletteText.Should().Be("#AABBCC");
        item.Swatches.Should().ContainSingle().Which.Hex.Should().Be("#AABBCC");
    }

    [Fact]
    public void StyleSuggestionLists_AreDocSourcedAndUsable()
    {
        var sut = CreateSut();

        // Medium is the trained vocabulary from ideogram4 docs/prompting.md — pin it exactly.
        sut.MediumSuggestions.Should().Equal(
            "photograph", "illustration", "3d_render", "painting", "graphic_design");

        foreach (var list in new[]
                 {
                     sut.ArtStyleSuggestions, sut.PhotoStyleSuggestions,
                     sut.LightingSuggestions, sut.AestheticsSuggestions
                 })
        {
            list.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void StylePaletteSwatches_RecomputeWhenPaletteTextChanges()
    {
        // The Swatches notification is what refreshes the chip FlexLayout while the user types
        // (the Entry's view->VM half is an explicit TextChanged push in the page code-behind).
        var sut = CreateSut();
        var notified = false;
        sut.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(sut.StylePaletteSwatches);

        sut.StylePaletteText = "#AABBCC";

        notified.Should().BeTrue();
        sut.StylePaletteSwatches.Should().ContainSingle().Which.Hex.Should().Be("#AABBCC");
    }

    [Fact]
    public void ApplyToGenerator_WithoutGenerator_IsANoOp()
    {
        // The generator-coupled Apply behavior is pinned in GeneratorViewModelTests
        // (EditorApply_*); stand-alone construction must simply do nothing.
        var sut = CreateSut();
        sut.SelectedResolution = "1440x2880";

        sut.Invoking(s => s.ApplyToGenerator("{\"a\":1}")).Should().NotThrow();
    }

    // ---- Region-aware enrichment ---------------------------------------------------------

    /// <summary>
    /// A structure editor with the things a real one is handed: a host to read the provider lists through, and
    /// a store to remember through. A page whose Enrich button works but that has no settings to read is not a
    /// state the app ships in, so the default store has already been picked once - and it has to be, because
    /// Local has no default model by design and a builder that left the row empty would turn every success
    /// test below into a refusal test. The tests about not having picked anything bring their own store.
    /// </summary>
    private IdeogramStructureEditorViewModel CreateSut(
        IEnrichRegionsLlmService enrich,
        Mock<IUiStateStore>? store = null,
        GeneratorViewModel? generator = null) =>
        new(_fileService.Object, _fileLauncher.Object, NullLogger<IdeogramStructureEditorViewModel>.Instance,
            enrich: enrich,
            generator: generator ?? BareGenerator.New(),
            uiStateStore: (store ?? EnrichmentStore()).Object);

    /// <summary>First launch that has been used once: this role on Local, with a model picked.</summary>
    private static Mock<IUiStateStore> EnrichmentStore()
    {
        var store = EmptyUiStateStore.New();
        store.Setup(s => s.LoadLlmModel(ModelRole.Enrich, ModelProvider.Local)).Returns("llama3.1");
        return store;
    }

    /// <summary>Move the row the way a user does: provider first, then a model out of that provider's list.</summary>
    private static void PickEnrichment(IdeogramStructureEditorViewModel sut, ModelProvider provider, string model)
    {
        var row = sut.Enrichment
            ?? throw new InvalidOperationException("This page was built with no model settings to read.");
        row.SelectedProvider = provider;
        row.SelectedModel = model;
    }

    /// <summary>
    /// Enrichment that writes down who it was asked. A bare mock answers null, and a null result would be a
    /// NullReferenceException in the page rather than a finding; what these tests care about is the target the
    /// page resolved, which a setup-then-verify mock can assert but not show.
    /// </summary>
    private sealed class RecordingEnrichService(LlmVariantResult? reply = null) : IEnrichRegionsLlmService
    {
        public List<LlmTarget> Targets { get; } = [];

        public Task<LlmVariantResult> EnrichAsync(
            V4JsonPrompt baseCaption, LlmTarget target, CancellationToken ct = default)
        {
            Targets.Add(target);
            return Task.FromResult(reply ?? LlmVariantResult.Ok(baseCaption, "Enriched: x"));
        }
    }

    private static V4JsonPrompt EnrichedOf()
    {
        var enriched = V4JsonPromptSerializer.Deserialize(SampleJson);
        enriched.CompositionalDeconstruction.Elements[0].Desc = "a lighthouse on the left, set against the navy sky";
        enriched.CompositionalDeconstruction.Elements[1].Desc = "headline across the top band";
        return enriched;
    }

    [Fact]
    public async Task EnrichFromLayout_InvalidModel_ReportsValidationAndNeverCallsTheService()
    {
        var enrich = new Mock<IEnrichRegionsLlmService>();
        var sut = CreateSut(enrich.Object); // empty HLD/background → invalid

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.HasEnrichPreview.Should().BeFalse();
        enrich.Verify(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnrichFromLayout_Success_PopulatesPreview_WithoutMutatingDescsYet()
    {
        var enrich = new Mock<IEnrichRegionsLlmService>();
        enrich.Setup(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(LlmVariantResult.Ok(EnrichedOf(), "Enriched: x"));
        var sut = CreateSut(enrich.Object);
        sut.LoadFromJson(SampleJson);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        sut.HasEnrichPreview.Should().BeTrue();
        sut.EnrichPreview.Should().HaveCount(2);
        sut.EnrichPreview[0].NewDesc.Should().Contain("on the left");
        sut.Elements[0].Desc.Should().Be("a lighthouse", "the preview must not touch the live elements until Accept");
    }

    [Fact]
    public async Task AcceptEnrich_CopiesNewDescsInOrder_PreservingBboxTextAndSelection()
    {
        var enrich = new Mock<IEnrichRegionsLlmService>();
        enrich.Setup(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(LlmVariantResult.Ok(EnrichedOf(), "Enriched: x"));
        var sut = CreateSut(enrich.Object);
        sut.LoadFromJson(SampleJson);
        var selected = sut.SelectedElement;

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);
        sut.AcceptEnrichCommand.Execute(null);

        sut.Elements[0].Desc.Should().Be("a lighthouse on the left, set against the navy sky");
        sut.Elements[1].Desc.Should().Be("headline across the top band");
        sut.Elements[1].Text.Should().Be("BEACON");
        sut.SelectedElement.Should().BeSameAs(selected);
        sut.HasEnrichPreview.Should().BeFalse();

        // The bboxes survive the round-trip; only descs changed.
        var rebuilt = sut.BuildModel();
        rebuilt.CompositionalDeconstruction.Elements[0].Bbox.Should().Equal(100, 200, 300, 400);
        rebuilt.CompositionalDeconstruction.Elements[1].Bbox.Should().Equal(0, 0, 100, 1000);
    }

    [Fact]
    public async Task DiscardEnrich_LeavesDescsUnchanged()
    {
        var enrich = new Mock<IEnrichRegionsLlmService>();
        enrich.Setup(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(LlmVariantResult.Ok(EnrichedOf(), "Enriched: x"));
        var sut = CreateSut(enrich.Object);
        sut.LoadFromJson(SampleJson);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);
        sut.DiscardEnrichCommand.Execute(null);

        sut.Elements[0].Desc.Should().Be("a lighthouse");
        sut.HasEnrichPreview.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichFromLayout_ServiceFailure_SetsErrorStatus_ElementsUntouched()
    {
        var enrich = new Mock<IEnrichRegionsLlmService>();
        enrich.Setup(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(LlmVariantResult.Fail("model said no"));
        var sut = CreateSut(enrich.Object);
        sut.LoadFromJson(SampleJson);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.StatusMessage.Should().Contain("model said no");
        sut.HasEnrichPreview.Should().BeFalse();
        sut.Elements[0].Desc.Should().Be("a lighthouse");
    }

    [Fact]
    public async Task EnrichFromLayout_TogglesIsEnriching_AroundTheCall()
    {
        var gate = new TaskCompletionSource<LlmVariantResult>();
        var enrich = new Mock<IEnrichRegionsLlmService>();
        enrich.Setup(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()))
              .Returns(gate.Task);
        var sut = CreateSut(enrich.Object);
        sut.LoadFromJson(SampleJson);

        var run = sut.EnrichFromLayoutCommand.ExecuteAsync(null);
        sut.IsEnriching.Should().BeTrue();

        gate.SetResult(LlmVariantResult.Ok(EnrichedOf(), "Enriched: x"));
        await run;

        sut.IsEnriching.Should().BeFalse();
    }

    [Fact]
    public async Task CancelEnrich_CancelsTheCall_WithInfoStatus()
    {
        var gate = new TaskCompletionSource();
        var enrich = new Mock<IEnrichRegionsLlmService>();
        enrich.Setup(s => s.EnrichAsync(It.IsAny<V4JsonPrompt>(), It.IsAny<LlmTarget>(), It.IsAny<CancellationToken>()))
              .Returns(async (V4JsonPrompt _, LlmTarget _, CancellationToken ct) =>
              {
                  await gate.Task;
                  ct.ThrowIfCancellationRequested();
                  return LlmVariantResult.Ok(EnrichedOf(), "x");
              });
        var sut = CreateSut(enrich.Object);
        sut.LoadFromJson(SampleJson);

        var run = sut.EnrichFromLayoutCommand.ExecuteAsync(null);
        sut.CancelEnrichCommand.Execute(null);
        gate.SetResult();
        await run;

        sut.StatusKind.Should().Be(StatusKind.Info);
        sut.StatusMessage.Should().Contain("cancelled");
        sut.IsEnriching.Should().BeFalse();
    }

    // ---- The row's own decision ---------------------------------------------------
    //
    // Enrichment used to answer with a tier: three words, two of which named a Claude model, no way to say
    // "OpenRouter", and no memory - it opened on the paid model on every visit. What replaces it is a row
    // with a provider, a model, a memory, and three different ways to have nobody to call: no settings, no
    // model, and a settings store that has never been asked about this role.

    [Fact]
    public void Enrichment_OnFirstLaunchOpensOnLocal_BecauseEnrichingNobodyAskedForShouldNotBillAnyone()
    {
        var sut = CreateSut(new RecordingEnrichService(), EmptyUiStateStore.New());

        sut.Enrichment!.SelectedProvider.Should().Be(ModelProvider.Local,
            "the tier this replaces defaulted to a paid Claude model, and it did so again on every navigation");
        sut.Enrichment.SelectedModel.Should().BeNull(
            "Local has no default model to guess, and guessing one is how a page calls a server nobody picked");
    }

    [Fact]
    public void Enrichment_IsOfferedEveryProviderThatReadsText()
    {
        var sut = CreateSut(new RecordingEnrichService());

        sut.Enrichment!.Providers.Should().Equal(
            new[] { ModelProvider.Local, ModelProvider.Anthropic, ModelProvider.OpenRouter },
            "enrichment sends text, so nothing rules a provider out for it - unlike an observer, which cannot "
            + "use Anthropic in this build and is not offered it");
    }

    [Fact]
    public void Enrichment_OpensOnWhatThisRolePickedLastTime()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmProvider(ModelRole.Enrich, It.IsAny<ModelProvider>()))
            .Returns(ModelProvider.OpenRouter);
        store.Setup(s => s.LoadLlmModel(ModelRole.Enrich, ModelProvider.OpenRouter))
            .Returns("deepseek/deepseek-chat");

        var sut = CreateSut(new RecordingEnrichService(), store);

        sut.Enrichment!.SelectedProvider.Should().Be(ModelProvider.OpenRouter,
            "a row that resets on every visit is a row whose answer the page never used");
        sut.Enrichment.SelectedModel.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public void PickingAnEnrichmentModelRemembersItUnderThisRoleAlone()
    {
        var store = EmptyUiStateStore.New();
        var sut = CreateSut(new RecordingEnrichService(), store);

        PickEnrichment(sut, ModelProvider.OpenRouter, "deepseek/deepseek-chat");

        store.Verify(s => s.PersistLlmProvider(ModelRole.Enrich, ModelProvider.OpenRouter), Times.Once);
        store.Verify(s => s.PersistLlmModel(ModelRole.Enrich, ModelProvider.OpenRouter, "deepseek/deepseek-chat"),
            Times.Once);
        store.Verify(
            s => s.PersistLlmModel(It.Is<ModelRole>(r => r != ModelRole.Enrich), It.IsAny<ModelProvider>(),
                It.IsAny<string>()), Times.Never,
            "four roles share this store and this page owns one of them; writing another role's model is how "
            + "one picker came to move four pages");
    }

    [Fact]
    public async Task Enrich_WithNoModelPicked_SendsNoModelAndShowsWhoseAnswerItAskedFor()
    {
        // The page does not check for a missing model, and the page does not word that failure. The service
        // words it per provider - naming Ollama for Local, Claude for Anthropic - and a page repeating the
        // question in its own words would be a second answer to one question, which is how two wordings
        // start drifting. What the page owes is a target that admits it has no model, and the sentence it
        // gets handed back shown as it came.
        var enrich = new RecordingEnrichService(
            LlmVariantResult.Fail("No local Ollama model selected - pick one in the structure editor."));
        var sut = CreateSut(enrich, EmptyUiStateStore.New());
        sut.LoadFromJson(SampleJson);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        var sent = enrich.Targets.Should().ContainSingle().Subject;
        sent.Provider.Should().Be(ModelProvider.Local);
        sent.HasModel.Should().BeFalse("there is nothing to call, and a row must not invent a model to fill in");
        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.StatusMessage.Should().Be("No local Ollama model selected - pick one in the structure editor.",
            "what the service said is what the page shows, unchanged");
        sut.HasEnrichPreview.Should().BeFalse();
    }

    [Fact]
    public async Task Enrich_WithNoSettingsToRead_RefusesRatherThanGuessing()
    {
        // A structure editor built without a host - and the standalone construction tests above show that is
        // a real shape - cannot know who would answer. Guessing would bill somebody's key for a description
        // nobody chose, which is the defect this whole refactor exists to kill.
        var enrich = new RecordingEnrichService();
        var sut = new IdeogramStructureEditorViewModel(
            _fileService.Object, _fileLauncher.Object, NullLogger<IdeogramStructureEditorViewModel>.Instance,
            enrich: enrich);
        sut.LoadFromJson(SampleJson);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        sut.Enrichment.Should().BeNull("there was no host to build a row from");
        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.StatusMessage.Should().Contain("unavailable");
        enrich.Targets.Should().BeEmpty();
    }

    [Fact]
    public async Task Enrich_HandsTheRowLocalPickToTheService_AtTheUrlTheSettingsName()
    {
        var generator = BareGenerator.New();
        generator.Settings.OllamaBaseUrl = "http://ollama.example:11434";
        var enrich = new RecordingEnrichService();
        var sut = CreateSut(enrich, EnrichmentStore(), generator);
        sut.LoadFromJson(SampleJson);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        var sent = enrich.Targets.Should().ContainSingle().Subject;
        sent.Provider.Should().Be(ModelProvider.Local);
        sent.ModelId.Should().Be("llama3.1", "the row is the only model enrichment reads now");
        sent.BaseUrl.Should().Be("http://ollama.example:11434",
            "which Ollama answers is a setting, not something the service works out for itself");
    }

    [Fact]
    public async Task Enrich_HandsTheRowAnthropicPickToTheService_WithNoServerAddressAttached()
    {
        var enrich = new RecordingEnrichService();
        var sut = CreateSut(enrich);
        sut.LoadFromJson(SampleJson);
        PickEnrichment(sut, ModelProvider.Anthropic, AnthropicModels.Sonnet);

        await sut.EnrichFromLayoutCommand.ExecuteAsync(null);

        var sent = enrich.Targets.Should().ContainSingle().Subject;
        sent.Provider.Should().Be(ModelProvider.Anthropic);
        sent.ModelId.Should().Be(AnthropicModels.Sonnet);
        sent.BaseUrl.Should().BeEmpty(
            "Claude answers at one known endpoint; carrying the local Ollama address along would be a way to "
            + "send an Anthropic key to a LAN server");
    }

    [Theory]
    [InlineData(ModelProvider.Local, "Ollama")]
    [InlineData(ModelProvider.Anthropic, "Anthropic")]
    [InlineData(ModelProvider.OpenRouter, "OpenRouter")]
    public void EnrichmentHelper_NamesWhoAnswers_AndOnlyLocalClaimsToBeFree(ModelProvider provider, string brand)
    {
        var sut = CreateSut(new RecordingEnrichService());

        sut.Enrichment!.SelectedProvider = provider;
        var helper = sut.Enrichment.Helper;

        helper.Should().NotBeNull(
            "a row that switches provider has something to say about the provider it switched to, or it "
            + "should have said nothing at all");
        helper.Should().Contain(brand, "helper text that never names who answers is decoration beside a picker");
        helper!.ToLowerInvariant().Contains("free").Should().Be(provider == ModelProvider.Local,
            "free is a claim only the local row may make - and the money question is the whole reason this is "
            + "the one card the page leaves open");
    }
}
