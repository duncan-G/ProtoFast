using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Tree;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The materializer is where paragraph coverage stops being a check and becomes a property
/// (orchestrator plan §4.3), so it is tested directly and exhaustively. Every test here is a
/// plan a model could plausibly return.
/// </summary>
public class OrchestratedTreeTests
{
    /// <summary>
    /// Two windows, four sections. Window 1: "Introduction" (P00001) and "Methods" (P00002).
    /// Window 2: "Schedules" (P00003) and an untitled continuation (P00004).
    /// </summary>
    private static Dictionary<int, SectionNode> TwoWindows() => new()
    {
        [0] = Root(
            Leaf("Introduction", "L000001", ["P00001"]),
            Leaf("Methods", "L000002", ["P00002"])),
        [1] = Root(
            Leaf("Schedules", "L000003", ["P00003"]),
            Leaf("Continued", null, ["P00004"], inferred: true)),
    };

    private static readonly string[] AllParagraphs = ["P00001", "P00002", "P00003", "P00004"];

    [Fact]
    public void AStraightPlacementCoversEveryParagraphAndRenumbersInDocumentOrder()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]),
            Row(1, ["W00001:n1"]),
            Row(1, ["W00001:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Equal("Report", result.Root!.Title);
        Assert.Equal(
            ["Introduction", "Methods", "Schedules", "Continued"],
            result.Root.Children.Select(c => c.Title));

        // Ids are minted by code in document order, so S0000 is always the first section a reader
        // sees regardless of which window produced it.
        Assert.Equal(
            ["S0000", "S0001", "S0002", "S0003", "S0004"],
            result.Root.Descend().Select(n => n.SectionId));
    }

    [Fact]
    public void AMergeAcrossAWindowBoundaryBecomesOneSection()
    {
        // The case the splice cannot do: "Schedules" ran past the end of window 2 and window 3
        // titled its remainder itself, having never seen the heading.
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]),
            Row(1, ["W00001:n1", "W00001:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.True(result.Success, result.Validation.ErrorReport);

        var merged = result.Root!.Children[^1];
        Assert.Equal("Schedules", merged.Title);
        Assert.Equal("L000003", merged.HeadingLineId);
        Assert.Equal(["P00003", "P00004"], merged.ParagraphIds);
    }

    [Fact]
    public void AGroupRowIntroducesAParentTheSourceHasNoHeadingFor()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, [], "Part II"),
            Row(2, ["W00000:n2"]),
            Row(2, ["W00001:n1"]),
            Row(2, ["W00001:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.True(result.Success, result.Validation.ErrorReport);

        var part2 = result.Root!.Children[1];
        Assert.Equal("Part II", part2.Title);
        Assert.True(part2.TitleInferred, "a parent nobody wrote a heading for is an inferred title");
        Assert.Null(part2.HeadingLineId);
        Assert.Equal(3, part2.Children.Count);
        Assert.Equal(2, part2.Level);
    }

    [Fact]
    public void RetitlingANodeWithoutAHeadingIsAllowedAndMarksTheTitleInferred()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]),
            Row(1, ["W00001:n1"]),
            Row(1, ["W00001:n2"], "Schedules (continued)"));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Equal("Schedules (continued)", result.Root!.Children[^1].Title);
        Assert.True(result.Root.Children[^1].TitleInferred);
    }

    [Fact]
    public void RetitlingAHeadingAnchoredNodeIsRejected()
    {
        // heading-anchor forbids a node that both claims an inferred title and points at a source
        // line. Rejecting the row is better than silently dropping one of the two claims.
        var plan = Plan(
            Row(1, ["W00000:n1"], "Preface"),
            Row(1, ["W00000:n2"]),
            Row(1, ["W00001:n1"]),
            Row(1, ["W00001:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("L000001", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void PlacingAParentAndItsChildIsRejectedRatherThanDuplicated()
    {
        var windows = new Dictionary<int, SectionNode>
        {
            [0] = Root(new SectionNode(
                "s", "Methods", false, "L000001", 2,
                [Leaf("Sampling", "L000002", ["P00001"]), Leaf("Analysis", "L000003", ["P00002"])],
                [])),
        };

        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, windows, ["P00001", "P00002"], "Report");

        Assert.False(result.Success);
        Assert.Contains("inside it", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanThatOrphansAParagraphNamesIt()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]),
            Row(1, ["W00001:n1"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("P00004", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInventedReferenceIsRejected()
    {
        var plan = Plan(Row(1, ["W00009:n3"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("may not be invented", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippingADepthLevelIsRejected()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(3, ["W00000:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("at most one", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupWithNothingUnderItIsRejected()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]),
            Row(1, ["W00001:n1"]),
            Row(1, ["W00001:n2"]),
            Row(1, [], "Appendices"));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("nothing is nested under it", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void NestingUnderANodeThatAlreadyCarriesItsOwnSubtreeIsRejected()
    {
        // A window node arrives with its own shape. Hanging further rows off it would put the
        // node's paragraphs beside child sections, which tree-shape forbids — so the plan has to
        // say what it means: group these under a new titled parent.
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(2, ["W00000:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("nest under a titled parent", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void PlacingTheSameNodeTwiceIsRejected()
    {
        var plan = Plan(
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n1"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("already placed", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void ParagraphsPlacedOutOfDocumentOrderAreRejected()
    {
        var plan = Plan(
            Row(1, ["W00001:n1"]),
            Row(1, ["W00001:n2"]),
            Row(1, ["W00000:n1"]),
            Row(1, ["W00000:n2"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, TwoWindows(), AllParagraphs, "Report");

        Assert.False(result.Success);
        Assert.Contains("precedes it in the document", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void MergingASubtreeWithALeafKeepsTheSubtreesShape()
    {
        var windows = new Dictionary<int, SectionNode>
        {
            [0] = Root(new SectionNode(
                "s", "Schedules", false, "L000001", 2,
                [Leaf("Schedule A", "L000002", ["P00001"])],
                [])),
            [1] = Root(Leaf("Continued", null, ["P00002"], inferred: true)),
        };

        var plan = Plan(Row(1, ["W00000:n1", "W00001:n1"]));

        var result = OrchestratedTreeMaterializer.Apply(plan, windows, ["P00001", "P00002"], "Report");

        Assert.True(result.Success, result.Validation.ErrorReport);

        var merged = result.Root!.Children[0];
        Assert.Equal("Schedules", merged.Title);
        Assert.Equal(["Schedule A", "Continued"], merged.Children.Select(c => c.Title));
        Assert.Empty(merged.ParagraphIds);
    }

    [Fact]
    public void NodeReferencesRoundTrip()
    {
        Assert.True(NodeRefs.TryParse(NodeRefs.For(12, 345), out var window, out var node));
        Assert.Equal(12, window);
        Assert.Equal(345, node);

        Assert.False(NodeRefs.TryParse("W00001", out _, out _));
        Assert.False(NodeRefs.TryParse("nonsense", out _, out _));
        Assert.False(NodeRefs.TryParse(null, out _, out _));
    }

    private static AssemblyPlan Plan(params AssemblyRow[] rows) => new() { Outline = rows };

    private static AssemblyRow Row(int depth, string[] sources, string title = "") =>
        new() { Depth = depth, Sources = sources, Title = title };

    /// <summary>A window's reply: its own synthetic root (n0) over the sections it found.</summary>
    private static SectionNode Root(params SectionNode[] children) =>
        new("s", "Window", true, null, 1, children, []);

    private static SectionNode Leaf(string title, string? headingLineId, string[] paragraphs, bool inferred = false) =>
        new("s", title, inferred || headingLineId is null, headingLineId, 2, [], paragraphs);
}
