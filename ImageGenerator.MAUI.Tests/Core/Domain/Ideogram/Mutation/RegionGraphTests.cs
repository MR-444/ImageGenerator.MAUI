using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate.
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Pure-geometry tests for <see cref="RegionGraph"/>. The headline property under test: it emits only
/// facts geometry can prove and NEVER derives front/behind from element list order — overlap depth is a
/// SOFT cue keyed to box size + vertical position, invariant to the order elements appear in.
/// </summary>
public sealed class RegionGraphTests
{
    private static Element Obj(int[]? bbox, string desc = "thing") =>
        new() { Type = Element.ObjType, Bbox = bbox, Desc = desc };

    private static Element Text(int[]? bbox, string text) =>
        new() { Type = Element.TextType, Bbox = bbox, Text = text, Desc = "label" };

    private static ElementRegionFact Fact(RegionGraphResult r, int index) => r.Elements.Single(e => e.Index == index);

    // ---- Horizontal zone -----------------------------------------------------------------

    [Theory]
    [InlineData(200, 400, HorizontalZone.Left)]   // cx 300
    [InlineData(450, 550, HorizontalZone.Center)] // cx 500
    [InlineData(600, 800, HorizontalZone.Right)]  // cx 700
    public void HorizontalZone_ClassifiesByCenterX(int xMin, int xMax, HorizontalZone expected)
    {
        var r = RegionGraph.Compute([Obj([400, xMin, 600, xMax])]);
        Fact(r, 0).Horizontal.Should().Be(expected);
    }

    // ---- Vertical band -------------------------------------------------------------------

    [Theory]
    [InlineData(0, 200, VerticalBand.Sky)]      // cy 100
    [InlineData(400, 600, VerticalBand.Middle)] // cy 500
    [InlineData(800, 1000, VerticalBand.Ground)]// cy 900
    public void VerticalBand_ClassifiesByCenterY(int yMin, int yMax, VerticalBand expected)
    {
        var r = RegionGraph.Compute([Obj([yMin, 400, yMax, 600])]);
        Fact(r, 0).Band.Should().Be(expected);
    }

    [Fact]
    public void SpansMultipleBands_TrueWhenBoxCrossesThirds()
    {
        var r = RegionGraph.Compute([Obj([100, 400, 900, 600])]);
        Fact(r, 0).SpansMultipleBands.Should().BeTrue();
    }

    // ---- Relative position ---------------------------------------------------------------

    [Fact]
    public void Position_LeftOf_FromCenters()
    {
        var r = RegionGraph.Compute([Obj([400, 0, 600, 200]), Obj([400, 800, 600, 1000])]);
        var rel = r.Relations.Single();
        rel.FromIndex.Should().Be(0);
        rel.Position.Should().Be(RelativePosition.LeftOf);
    }

    [Fact]
    public void Position_Above_FromCenters()
    {
        var r = RegionGraph.Compute([Obj([0, 400, 200, 600]), Obj([800, 400, 1000, 600])]);
        r.Relations.Single().Position.Should().Be(RelativePosition.Above);
    }

    [Fact]
    public void Position_Aligned_WhenCentersCoincide()
    {
        var r = RegionGraph.Compute([Obj([400, 400, 600, 600]), Obj([420, 420, 580, 580])]);
        r.Relations.Single().Position.Should().Be(RelativePosition.Aligned);
    }

    // ---- Support / contact ---------------------------------------------------------------

    [Fact]
    public void Support_RestingOn_WhenBottomMeetsTopWithOverlap()
    {
        var r = RegionGraph.Compute([Obj([300, 400, 500, 600]), Obj([500, 400, 700, 600])]);
        var rel = r.Relations.Single();
        rel.Support.Should().Be(SupportRelation.RestingOn);
        rel.FromIndex.Should().Be(0);
        rel.ToIndex.Should().Be(1);
    }

    [Fact]
    public void Support_None_WhenEdgesMeetButNoHorizontalOverlap()
    {
        var r = RegionGraph.Compute([Obj([300, 0, 500, 200]), Obj([500, 800, 700, 1000])]);
        r.Relations.Single().Support.Should().Be(SupportRelation.None);
    }

    [Fact]
    public void Support_LeaningAgainst_WhenSideEdgesTouchWithVerticalOverlap()
    {
        var r = RegionGraph.Compute([Obj([300, 300, 700, 500]), Obj([300, 500, 700, 700])]);
        r.Relations.Single().Support.Should().Be(SupportRelation.LeaningAgainst);
    }

    [Fact]
    public void Support_Mounted_WhenSmallBoxSitsInsideLargerOne()
    {
        var r = RegionGraph.Compute([Obj([400, 400, 500, 600]), Obj([200, 200, 800, 800])]);
        r.Relations.Single().Support.Should().Be(SupportRelation.Mounted);
    }

    // ---- Overlap fact + soft depth cue ---------------------------------------------------

    [Fact]
    public void Overlap_RecordsIou_WhenBoxesIntersect()
    {
        int[] a = [400, 0, 1000, 600];
        int[] b = [200, 300, 600, 700];
        var rel = RegionGraph.Compute([Obj(a), Obj(b)]).Relations.Single();
        rel.Overlaps.Should().BeTrue();
        rel.Iou.Should().BeApproximately(BboxMath.IoU(a, b), 1e-9);
    }

    [Fact]
    public void DepthCue_LargerAndLower_LeansNearer()
    {
        // a is bigger AND its bottom edge is lower in frame than b → from-a leans nearer.
        var r = RegionGraph.Compute([Obj([400, 0, 1000, 600]), Obj([200, 300, 600, 700])]);
        var rel = r.Relations.Single();
        rel.FromIndex.Should().Be(0);
        rel.FromDepthCue.Should().Be(DepthCue.LikelyNearer);
    }

    [Fact]
    public void DepthCue_LargerButHigher_IsAmbiguous()
    {
        var r = RegionGraph.Compute([Obj([0, 0, 400, 800]), Obj([300, 300, 700, 600])]);
        r.Relations.Single().FromDepthCue.Should().Be(DepthCue.Ambiguous);
    }

    [Fact]
    public void DepthCue_EqualSize_IsAmbiguous()
    {
        var r = RegionGraph.Compute([Obj([0, 0, 400, 400]), Obj([100, 100, 500, 500])]);
        r.Relations.Single().FromDepthCue.Should().Be(DepthCue.Ambiguous);
    }

    [Fact]
    public void DepthCue_Null_WhenNotOverlapping()
    {
        var r = RegionGraph.Compute([Obj([0, 0, 200, 200]), Obj([0, 800, 200, 1000])]);
        var rel = r.Relations.Single();
        rel.Overlaps.Should().BeFalse();
        rel.FromDepthCue.Should().BeNull();
    }

    // ---- Order is NOT depth (the headline invariant) -------------------------------------

    [Fact]
    public void DepthVerdict_IsInvariantToElementOrder()
    {
        int[] big = [400, 0, 1000, 600];   // larger + lower → the nearer box
        int[] small = [200, 300, 600, 700];

        var forward = RegionGraph.Compute([Obj(big), Obj(small)]).Relations.Single();
        var reversed = RegionGraph.Compute([Obj(small), Obj(big)]).Relations.Single();

        // Map the cue back to the physical box; the verdict (big is nearer) must not flip with order.
        NearerTag(forward, ["big", "small"]).Should().Be("big");
        NearerTag(reversed, ["small", "big"]).Should().Be("big");
    }

    private static string NearerTag(RegionRelation rel, string[] tagsByIndex) => rel.FromDepthCue switch
    {
        DepthCue.LikelyNearer => tagsByIndex[rel.FromIndex],
        DepthCue.LikelyFarther => tagsByIndex[rel.ToIndex],
        _ => "ambiguous"
    };

    // ---- Unplaced elements ---------------------------------------------------------------

    [Fact]
    public void NoBbox_IsUnplaced_AndAppearsInNoRelation()
    {
        var r = RegionGraph.Compute([Obj([400, 400, 600, 600]), Text(null, "OPEN")]);
        var unplaced = Fact(r, 1);
        unplaced.IsPlaced.Should().BeFalse();
        unplaced.Horizontal.Should().BeNull();
        unplaced.Band.Should().BeNull();
        r.Relations.Should().NotContain(rel => rel.FromIndex == 1 || rel.ToIndex == 1);
    }

    [Fact]
    public void DegenerateBox_IsTreatedAsUnplaced()
    {
        var r = RegionGraph.Compute([Obj([400, 400, 410, 600])]); // 10px tall < MinPlacedSpan
        Fact(r, 0).IsPlaced.Should().BeFalse();
    }

    // ---- Determinism ---------------------------------------------------------------------

    [Fact]
    public void Compute_IsDeterministic()
    {
        var elements = new List<Element>
        {
            Obj([300, 400, 500, 600]),
            Obj([500, 400, 700, 600]),
            Obj([0, 0, 200, 200]),
        };

        var a = RegionGraph.Compute(elements);
        var b = RegionGraph.Compute(elements);

        a.Elements.Should().BeEquivalentTo(b.Elements, o => o.WithStrictOrdering());
        a.Relations.Should().BeEquivalentTo(b.Relations, o => o.WithStrictOrdering());
    }
}
