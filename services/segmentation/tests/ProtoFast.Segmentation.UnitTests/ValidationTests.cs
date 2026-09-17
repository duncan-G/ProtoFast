using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

public class ValidationTests
{
    [Fact]
    public void IdCoverageRejectsInventedMissingAndDuplicateIds()
    {
        var result = Checks.CheckIdCoverage(
            ["P00000", "P00001", "P00002"],
            ["P00000", "P00000", "P99999"]);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("unknown id 'P99999'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("duplicate id 'P00000'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("missing id 'P00001'", StringComparison.Ordinal));
    }

    [Fact]
    public void IdCoverageRejectsOutOfOrderIds()
    {
        var result = Checks.CheckIdCoverage(["P00000", "P00001"], ["P00001", "P00000"]);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("precedes it in the document", StringComparison.Ordinal));
    }

    [Fact]
    public void TreeShapeRejectsANodeWithBothChildrenAndParagraphs()
    {
        var root = new SectionNode("S0000", "Document", true, null, 1,
            [new SectionNode("S0001", "Child", false, null, 2, [], ["P00001"])],
            ["P00000"]);

        var result = Checks.CheckTreeShape(root);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("never both", StringComparison.Ordinal));
    }

    [Fact]
    public void TreeShapeRejectsEmptySectionsAndWrongLevels()
    {
        var root = new SectionNode("S0000", "Document", true, null, 1,
        [
            new SectionNode("S0001", "Empty", false, null, 2, [], []),
            new SectionNode("S0002", "Wrong level", false, null, 5, [], ["P00000"]),
        ], []);

        var result = Checks.CheckTreeShape(root);

        Assert.Contains(result.Errors, e => e.Contains("is empty", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("level 5 at depth 2", StringComparison.Ordinal));
    }

    [Fact]
    public void TreeShapeAcceptsAWellFormedTree()
    {
        var root = new SectionNode("S0000", "Document", true, null, 1,
        [
            new SectionNode("S0001", "Overview", true, null, 2, [], ["P00000", "P00001"]),
            new SectionNode("S0002", "Methods", false, "L000004", 2, [], ["P00002"]),
        ], []);

        Assert.True(Checks.CheckTreeShape(root).Passed);
    }

    [Fact]
    public void ContiguityRejectsNonContiguousAndOverlappingSections()
    {
        var paragraphs = Paragraphs(5);
        var root = new SectionNode("S0000", "Document", true, null, 1,
        [
            // P00000 and P00003 are not adjacent in the document.
            new SectionNode("S0001", "A", true, null, 2, [], ["P00000", "P00003"]),
            // P00003 is claimed twice.
            new SectionNode("S0002", "B", true, null, 2, [], ["P00003"]),
        ], []);

        var result = Checks.CheckContiguity(root, paragraphs);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("not contiguous", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("appears in both", StringComparison.Ordinal));
    }

    [Fact]
    public void ContiguityRejectsATreeThatVisitsParagraphsOutOfDocumentOrder()
    {
        var paragraphs = Paragraphs(4);
        var root = new SectionNode("S0000", "Document", true, null, 1,
        [
            new SectionNode("S0001", "Later", true, null, 2, [], ["P00002", "P00003"]),
            new SectionNode("S0002", "Earlier", true, null, 2, [], ["P00000", "P00001"]),
        ], []);

        var result = Checks.CheckContiguity(root, paragraphs);

        Assert.Contains(result.Errors, e => e.Contains("out of document order", StringComparison.Ordinal));
    }

    [Fact]
    public void HeadingAnchorRejectsUnknownAndReusedAnchors()
    {
        var root = new SectionNode("S0000", "Document", true, null, 1,
        [
            new SectionNode("S0001", "A", false, "L000001", 2, [], ["P00000"]),
            new SectionNode("S0002", "B", false, "L000001", 2, [], ["P00001"]),
            new SectionNode("S0003", "C", false, "L999999", 2, [], ["P00002"]),
        ], []);

        var result = Checks.CheckHeadingAnchor(root, new HashSet<string>(StringComparer.Ordinal) { "L000001" });

        Assert.Contains(result.Errors, e => e.Contains("anchors both S0001 and S0002", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("is not a line labelled HEAD", StringComparison.Ordinal));
    }

    [Fact]
    public void HeadingAnchorRejectsAnInferredTitleOnASourceHeading()
    {
        // A node cannot both anchor a real heading and claim its title was inferred: one of the
        // two is a lie, and ThePlot marks inferred titles differently for the reader.
        var root = new SectionNode("S0000", "A", true, "L000001", 1, [], ["P00000"]);

        var result = Checks.CheckHeadingAnchor(root, new HashSet<string>(StringComparer.Ordinal) { "L000001" });

        Assert.Contains(result.Errors, e => e.Contains("claims an inferred title", StringComparison.Ordinal));
    }

    [Fact]
    public void TrustedRespectRejectsALabelThatRemovesATrustedBoundary()
    {
        var run = Fixtures.Run("# Methods\n\nBody text that follows.\n");
        var demoted = run.Labels
            .Select(l => l.Label == LineLabel.Head ? l with { Label = LineLabel.Cont } : l)
            .ToList();

        var result = Checks.CheckTrustedRespect(run.Cleaning, demoted);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("cannot be removed", StringComparison.Ordinal));
    }

    [Fact]
    public void LabelEnumRejectsOtherWithoutAKindAndHeadWithoutALevel()
    {
        var labels = new List<LineLabelResult>
        {
            new("L000000", LineLabel.Other, null, 0.9),
            new("L000001", LineLabel.Head, null, 0.9),
            new("L000002", LineLabel.Cont, null, 1.4),
        };

        var result = Checks.CheckLabelEnum(labels, requireHeadingLevels: true);

        Assert.Contains(result.Errors, e => e.Contains("requires a 'kind'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("no level in 1..6", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("outside 0..1", StringComparison.Ordinal));
    }

    [Fact]
    public void SizeBoundsFlagsOutliersAndHonoursWaivers()
    {
        var paragraphs = new List<Paragraph>
        {
            new("P00000", "L000000", "L000000", "short", 3, ParagraphKind.Body, "h"),
            new("P00001", "L000001", "L000001", "table", 3, ParagraphKind.Table, "h"),
        };
        var options = new ParagraphOptions { MinWords = 15, MaxWords = 300 };

        Assert.False(Checks.CheckSizeBounds(paragraphs, options).Passed);
        Assert.True(Checks.CheckSizeBounds(
            paragraphs, options, new HashSet<string>(StringComparer.Ordinal) { "P00000" }).Passed);
    }

    [Fact]
    public void EditLineageRejectsACycle()
    {
        var paragraphs = new List<Paragraph>
        {
            new("P00000a", "L000000", "L000000", "a", 1, ParagraphKind.Body, "h") { Lineage = ["P00000b"] },
            new("P00000b", "L000001", "L000001", "b", 1, ParagraphKind.Body, "h") { Lineage = ["P00000a"] },
        };

        var result = Checks.CheckEditLineage(paragraphs);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, e => e.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void TheErrorReportIsTruncatedForSystematicFailures()
    {
        var result = ValidationResult.Fail("x", Enumerable.Range(0, 100).Select(i => $"error {i}"));

        Assert.Contains("…and 80 more", result.ErrorReport, StringComparison.Ordinal);
    }

    private static List<Paragraph> Paragraphs(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i =>
            new Paragraph(Ids.Paragraph(i), Ids.Line(i), Ids.Line(i), $"paragraph {i}", 2, ParagraphKind.Body, "h")),
    ];
}
