using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
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
    public void BuildApplyRoute_CarriesJsonAndResolution()
    {
        var sut = CreateSut();
        sut.SelectedResolution = "1440x2880";

        var route = sut.BuildApplyRoute("{\"a\":1}");

        route.Should().StartWith("//MainPage?ideogramJson=");
        route.Should().Contain(Uri.EscapeDataString("{\"a\":1}"));
        route.Should().EndWith("&ideogramResolution=1440x2880");
    }
}
