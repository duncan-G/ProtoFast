using ProtoFast.Segmentation.Core.Evaluation;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The metrics gate CI merges (plan §26.6), so they are checked against hand-computed values —
/// a metric that is subtly wrong is worse than no metric, because it makes a regression invisible.
/// </summary>
public class MetricsTests
{
    [Fact]
    public void PkIsZeroForAPerfectSegmentation()
    {
        var reference = SegmentationMetrics.FromSegmentSizes([10, 10, 10]);
        Assert.Equal(0, SegmentationMetrics.Pk(reference, reference), 10);
        Assert.Equal(0, SegmentationMetrics.WindowDiff(reference, reference), 10);
    }

    [Fact]
    public void PkMatchesAHandComputedValue()
    {
        // 6 units. Reference splits after unit 2; hypothesis splits after unit 4. k = 6/2/2 = 2.
        // Positions i = 0..3 compare i and i+2:
        //   (0,2) ref differ / hyp same      -> error
        //   (1,3) ref differ / hyp same      -> error
        //   (2,4) ref same   / hyp differ    -> error
        //   (3,5) ref same   / hyp differ    -> error
        // 4 errors over (6 - 2) = 4 positions -> 1.0
        var reference = SegmentationMetrics.FromSegmentSizes([2, 4]);
        var hypothesis = SegmentationMetrics.FromSegmentSizes([4, 2]);

        Assert.Equal(2, SegmentationMetrics.DefaultWindow(reference));
        Assert.Equal(1.0, SegmentationMetrics.Pk(reference, hypothesis, 2), 10);
    }

    [Fact]
    public void WindowDiffPenalizesBoundaryCountErrorsPkCanMiss()
    {
        // Two boundaries close together inside the window: Pk's "same segment?" test at the
        // window ends is satisfied, WindowDiff's count test is not.
        var reference = SegmentationMetrics.FromSegmentSizes([4, 4]);
        var hypothesis = SegmentationMetrics.FromSegmentSizes([3, 1, 1, 3]);

        Assert.True(
            SegmentationMetrics.WindowDiff(reference, hypothesis, 2) > SegmentationMetrics.Pk(reference, hypothesis, 2));
    }

    [Fact]
    public void BoundaryF1CountsEachReferenceBoundaryOnce()
    {
        // Three hypothesis boundaries clustered around one reference boundary at index 4:
        // one is a hit within tolerance 1, the other two are false positives.
        var reference = SegmentationMetrics.FromSegmentSizes([4, 4]);
        var hypothesis = SegmentationMetrics.FromSegmentSizes([3, 1, 1, 3]);

        var tolerant = SegmentationMetrics.BoundaryF1(reference, hypothesis, tolerance: 1);

        Assert.Equal(1, tolerant.TruePositives);
        Assert.Equal(1.0 / 3, tolerant.Precision, 10);
        Assert.Equal(1.0, tolerant.Recall, 10);
    }

    [Fact]
    public void BoundaryF1IsPerfectForIdenticalSegmentations()
    {
        var segmentation = SegmentationMetrics.FromSegmentSizes([5, 5, 5]);
        Assert.Equal(1.0, SegmentationMetrics.BoundaryF1(segmentation, segmentation).F1, 10);
    }

    [Fact]
    public void MismatchedLengthsAreRejectedRatherThanScored()
    {
        Assert.Throws<ArgumentException>(() =>
            SegmentationMetrics.Pk(SegmentationMetrics.FromSegmentSizes([3]), SegmentationMetrics.FromSegmentSizes([4])));
    }

    [Fact]
    public void TreeEditDistanceIsZeroForIdenticalTrees()
    {
        var tree = Tree("A", ["P00000", "P00001"], "B", ["P00002"]);
        Assert.Equal(0, Core.Evaluation.TreeEditDistance.Normalized(tree, tree), 10);
    }

    [Fact]
    public void TreeEditDistanceGrowsWithStructuralDifference()
    {
        var reference = Tree("A", ["P00000", "P00001"], "B", ["P00002"]);
        var oneSectionMoved = Tree("A", ["P00000"], "B", ["P00001", "P00002"]);
        var flat = new SectionNode("S0000", "Document", true, null, 1, [], ["P00000", "P00001", "P00002"]);

        var near = Core.Evaluation.TreeEditDistance.Normalized(reference, oneSectionMoved);
        var far = Core.Evaluation.TreeEditDistance.Normalized(reference, flat);

        Assert.True(near > 0);
        Assert.True(far > near, $"flat {far} should be further than shifted {near}");
        Assert.InRange(far, 0, 1);
    }

    [Fact]
    public void PassAtKRequiresEveryRunToClearEveryThreshold()
    {
        var good = Metrics(pk: 0.05);
        var bad = Metrics(pk: 0.9);

        Assert.Equal(1.0, GoldEvaluator.PassAtK([[good, good, good]]), 10);
        Assert.Equal(0.0, GoldEvaluator.PassAtK([[good, good, bad]]), 10);
        Assert.Equal(0.5, GoldEvaluator.PassAtK([[good, good], [good, bad]]), 10);
    }

    private static SectionNode Tree(string a, string[] pa, string b, string[] pb) =>
        new("S0000", "Document", true, null, 1,
        [
            new SectionNode("S0001", a, false, null, 2, [], pa),
            new SectionNode("S0002", b, false, null, 2, [], pb),
        ], []);

    private static DocumentMetrics Metrics(double pk) => new(
        "doc", ConditionBucket.Clean, "unknown",
        Pk: pk,
        WindowDiff: 0.05,
        BoundaryF1Exact: PrecisionRecall.From(1, 1, 1),
        BoundaryF1Tolerant: PrecisionRecall.From(1, 1, 1),
        HeadingF1: PrecisionRecall.From(1, 1, 1),
        HeadingLevelAccuracy: 1.0,
        TreeEditDistance: 0.0,
        ArtifactRemoval: PrecisionRecall.From(1, 1, 1),
        TextIntegrityPassed: true,
        TreeSchemaPassed: true,
        CostUsd: 0,
        Duration: TimeSpan.Zero);
}
