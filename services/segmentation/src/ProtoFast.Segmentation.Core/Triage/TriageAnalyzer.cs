using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Triage;

/// <summary>Phase 2's output. An empty <see cref="SuspectRegions"/> is the cheap path (plan §9.4).</summary>
public sealed record TriageResult(
    IReadOnlyList<Region> Regions,
    ConditionBucket Condition,
    bool WholeDocumentSuspect)
{
    public IReadOnlyList<Region> SuspectRegions { get; } = [.. Regions.Where(r => r.IsSuspect)];

    /// <summary>
    /// True when phase 3 can be skipped entirely: the cleaning rules already produced a trusted
    /// boundary everywhere one belongs, so there is nothing for a model to decide. Most clean
    /// Markdown uploads take this path and finish in seconds with zero provider spend.
    /// </summary>
    public bool CanSkipLabeling => SuspectRegions.Count == 0;
}

/// <summary>
/// Phase 2: splits the cleaned document at trusted boundaries and decides which blocks a model
/// needs to look at. Everything it marks trusted is a block the pipeline never pays for.
/// </summary>
public sealed class TriageAnalyzer(PipelineOptions options)
{
    private readonly TriageOptions _triage = options.Triage;

    public TriageResult Analyze(CleaningResult cleaning, DocumentStatistics statistics, string family)
    {
        ArgumentNullException.ThrowIfNull(cleaning);

        var lines = cleaning.ContentLines;
        if (lines.Count == 0)
        {
            return new TriageResult([], ConditionBucket.Clean, WholeDocumentSuspect: false);
        }

        var boundaryLineIds = cleaning.Boundaries.Select(b => b.BeforeLineId).ToHashSet(StringComparer.Ordinal);
        var blocks = SplitAtBoundaries(lines, boundaryLineIds);
        var medianBlockWords = statistics.MedianBlockWords > 0
            ? statistics.MedianBlockWords
            : (int)Median(blocks.Select(b => (double)WordsIn(lines, b)).ToList());

        var regions = new List<Region>(blocks.Count);
        foreach (var block in blocks)
        {
            var reasons = SuspectReasons(lines, block, medianBlockWords, statistics);
            regions.Add(block with { IsSuspect = reasons.Count > 0, Reasons = reasons });
        }

        AddStructurelessReasons(regions, boundaryLineIds, lines);

        var suspectLines = regions.Where(r => r.IsSuspect).Sum(r => r.LineCount);
        var wholeDocumentSuspect = suspectLines > lines.Count * _triage.WholeDocSuspectFraction;

        if (wholeDocumentSuspect)
        {
            // Above the threshold, windowing the whole document is both simpler and better for
            // label consistency than stitching many small suspect regions together (plan §9.4).
            regions =
            [
                new Region(0, lines.Count, IsSuspect: true,
                    [.. regions.SelectMany(r => r.Reasons).Distinct().Append("whole-document")]),
            ];
        }

        var condition = ClassifyCondition(cleaning, statistics, family, suspectLines, lines.Count);
        return new TriageResult(regions, condition, wholeDocumentSuspect);
    }

    private List<string> SuspectReasons(
        IReadOnlyList<LineRecord> lines,
        Region block,
        int medianBlockWords,
        DocumentStatistics statistics)
    {
        var reasons = new List<string>();
        var text = string.Join(' ', lines.Skip(block.StartIndex).Take(block.LineCount).Select(l => l.Text));
        var words = TextMetrics.WordCount(text);

        var oversizeLimit = Math.Max(medianBlockWords * _triage.OversizeMedianMultiplier, _triage.OversizeMinWords);
        if (words > oversizeLimit)
        {
            reasons.Add($"oversize:{words}w>{oversizeLimit:0}");
        }

        // Only judge punctuation on blocks long enough for the rate to mean anything; a 6-word
        // caption with no full stop is not evidence of anything.
        if (words >= 40 && TextMetrics.TerminalPunctuationRate(text) < _triage.MinPunctuationPer40Words)
        {
            reasons.Add("low-punctuation");
        }

        if (HasHintConflict(lines, block, statistics))
        {
            reasons.Add("hint-conflict");
        }

        return reasons;
    }

    /// <summary>
    /// A converter heading hint the layout contradicts. Cleaning already refused to trust these;
    /// triage routes them to a model rather than silently dropping the claim.
    /// </summary>
    private bool HasHintConflict(IReadOnlyList<LineRecord> lines, Region block, DocumentStatistics statistics)
    {
        if (!statistics.HasLayout)
        {
            return false;
        }

        for (var i = block.StartIndex; i < block.EndIndexExclusive; i++)
        {
            var line = lines[i];
            if (line.Hint != SourceHint.MarkdownHeading || line.Layout is not { } layout)
            {
                continue;
            }

            if (layout.FontScale < options.Cleaning.HeadingFontScale
                && !(layout.IsBold && layout.GapAbove >= options.Cleaning.HeadingGapAbove))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A long stretch with no trusted boundary at all is suspect however well-punctuated it is:
    /// the source simply did not say where the paragraphs are.
    /// </summary>
    private void AddStructurelessReasons(
        List<Region> regions,
        HashSet<string> boundaryLineIds,
        IReadOnlyList<LineRecord> lines)
    {
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region.LineCount < _triage.StructurelessRunLines)
            {
                continue;
            }

            var interior = Enumerable
                .Range(region.StartIndex + 1, Math.Max(0, region.LineCount - 1))
                .Count(index => boundaryLineIds.Contains(lines[index].LineId));

            if (interior == 0)
            {
                regions[i] = region with
                {
                    IsSuspect = true,
                    Reasons = [.. region.Reasons, "structureless"],
                };
            }
        }
    }

    private ConditionBucket ClassifyCondition(
        CleaningResult cleaning,
        DocumentStatistics statistics,
        string family,
        int suspectLines,
        int totalLines)
    {
        if (family == FamilyDetector.Transcript)
        {
            return ConditionBucket.Conversational;
        }

        var suspectFraction = totalLines == 0 ? 0 : (double)suspectLines / totalLines;
        var headings = cleaning.Boundaries.Count(b => b.Kind == BoundaryKind.Heading);

        if (suspectFraction >= _triage.WholeDocSuspectFraction || (headings == 0 && statistics.PageCount > 5))
        {
            return ConditionBucket.Degraded;
        }

        return suspectFraction <= 0.05 && headings > 0 ? ConditionBucket.Clean : ConditionBucket.Partial;
    }

    private static List<Region> SplitAtBoundaries(IReadOnlyList<LineRecord> lines, HashSet<string> boundaryLineIds)
    {
        var blocks = new List<Region>();
        var start = 0;

        for (var i = 1; i < lines.Count; i++)
        {
            if (!boundaryLineIds.Contains(lines[i].LineId))
            {
                continue;
            }

            blocks.Add(new Region(start, i, IsSuspect: false, []));
            start = i;
        }

        blocks.Add(new Region(start, lines.Count, IsSuspect: false, []));
        return blocks;
    }

    private static int WordsIn(IReadOnlyList<LineRecord> lines, Region block) =>
        Enumerable.Range(block.StartIndex, block.LineCount).Sum(i => TextMetrics.WordCount(lines[i].Text));

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
