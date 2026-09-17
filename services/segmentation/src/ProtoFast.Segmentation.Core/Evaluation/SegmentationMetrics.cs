namespace ProtoFast.Segmentation.Core.Evaluation;

/// <summary>
/// The boundary metrics of Appendix D, in C# — the plan is explicit that the toolchain has no
/// Python (§26.2), and these have to run inside the CI evaluation gate that blocks merges.
///
/// <para>All of them take <em>segment id per unit</em> rather than a boundary set: that
/// representation makes "are these two positions in the same segment?" a comparison instead of a
/// search, which is the inner loop of both Pk and WindowDiff.</para>
/// </summary>
public static class SegmentationMetrics
{
    /// <summary>
    /// Pk (Beeferman et al., 1999). Slide a window of width <paramref name="k"/>; count the
    /// positions where reference and hypothesis disagree about whether the two ends fall in the
    /// same segment. Lower is better; 0 is perfect.
    /// </summary>
    public static double Pk(IReadOnlyList<int> reference, IReadOnlyList<int> hypothesis, int? k = null)
    {
        EnsureComparable(reference, hypothesis);
        var n = reference.Count;
        var width = k ?? DefaultWindow(reference);
        if (n <= width || width <= 0)
        {
            return 0;
        }

        var errors = 0;
        for (var i = 0; i + width < n; i++)
        {
            var referenceSame = reference[i] == reference[i + width];
            var hypothesisSame = hypothesis[i] == hypothesis[i + width];
            if (referenceSame != hypothesisSame)
            {
                errors++;
            }
        }

        return (double)errors / (n - width);
    }

    /// <summary>
    /// WindowDiff (Pevzner &amp; Hearst, 2002). Like Pk but counts boundaries inside the window,
    /// so it penalizes a hypothesis that puts the right number of boundaries in the wrong places
    /// — which Pk can miss.
    /// </summary>
    public static double WindowDiff(IReadOnlyList<int> reference, IReadOnlyList<int> hypothesis, int? k = null)
    {
        EnsureComparable(reference, hypothesis);
        var n = reference.Count;
        var width = k ?? DefaultWindow(reference);
        if (n <= width || width <= 0)
        {
            return 0;
        }

        var errors = 0;
        for (var i = 0; i + width < n; i++)
        {
            var referenceCount = CountBoundaries(reference, i, i + width);
            var hypothesisCount = CountBoundaries(hypothesis, i, i + width);
            if (referenceCount != hypothesisCount)
            {
                errors++;
            }
        }

        return (double)errors / (n - width);
    }

    /// <summary>
    /// Boundary precision/recall/F1 with a tolerance of <paramref name="tolerance"/> units. Each
    /// reference boundary may be matched at most once, so a hypothesis that stacks three
    /// boundaries next to one reference boundary scores one hit and two false positives.
    /// </summary>
    public static PrecisionRecall BoundaryF1(
        IReadOnlyList<int> reference,
        IReadOnlyList<int> hypothesis,
        int tolerance = 0)
    {
        EnsureComparable(reference, hypothesis);

        var referenceBoundaries = Boundaries(reference);
        var hypothesisBoundaries = Boundaries(hypothesis);
        var matched = new bool[referenceBoundaries.Count];
        var truePositives = 0;

        foreach (var position in hypothesisBoundaries)
        {
            for (var i = 0; i < referenceBoundaries.Count; i++)
            {
                if (matched[i] || Math.Abs(referenceBoundaries[i] - position) > tolerance)
                {
                    continue;
                }

                matched[i] = true;
                truePositives++;
                break;
            }
        }

        return PrecisionRecall.From(truePositives, hypothesisBoundaries.Count, referenceBoundaries.Count);
    }

    /// <summary>
    /// The window width both Pk and WindowDiff use by default: half the mean reference segment
    /// length, rounded, and at least 1.
    /// </summary>
    public static int DefaultWindow(IReadOnlyList<int> reference)
    {
        if (reference.Count == 0)
        {
            return 1;
        }

        var segments = reference.Distinct().Count();
        return Math.Max(1, (int)Math.Round((double)reference.Count / segments / 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Turns a list of segment sizes into the per-unit segment ids these metrics take.</summary>
    public static IReadOnlyList<int> FromSegmentSizes(IEnumerable<int> sizes)
    {
        var ids = new List<int>();
        var segment = 0;
        foreach (var size in sizes)
        {
            for (var i = 0; i < size; i++)
            {
                ids.Add(segment);
            }

            segment++;
        }

        return ids;
    }

    private static int CountBoundaries(IReadOnlyList<int> segmentIds, int from, int to)
    {
        var count = 0;
        for (var i = from + 1; i <= to; i++)
        {
            if (segmentIds[i] != segmentIds[i - 1])
            {
                count++;
            }
        }

        return count;
    }

    private static List<int> Boundaries(IReadOnlyList<int> segmentIds)
    {
        var boundaries = new List<int>();
        for (var i = 1; i < segmentIds.Count; i++)
        {
            if (segmentIds[i] != segmentIds[i - 1])
            {
                boundaries.Add(i);
            }
        }

        return boundaries;
    }

    private static void EnsureComparable(IReadOnlyList<int> reference, IReadOnlyList<int> hypothesis)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(hypothesis);

        if (reference.Count != hypothesis.Count)
        {
            throw new ArgumentException(
                $"Reference and hypothesis must cover the same {reference.Count} units; " +
                $"hypothesis has {hypothesis.Count}. A length mismatch means the two segmentations " +
                "are of different documents, which no metric can compare.",
                nameof(hypothesis));
        }
    }
}

public readonly record struct PrecisionRecall(double Precision, double Recall, double F1, int TruePositives)
{
    public static PrecisionRecall From(int truePositives, int predicted, int actual)
    {
        var precision = predicted == 0 ? (actual == 0 ? 1 : 0) : (double)truePositives / predicted;
        var recall = actual == 0 ? (predicted == 0 ? 1 : 0) : (double)truePositives / actual;
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        return new PrecisionRecall(precision, recall, f1, truePositives);
    }
}
