using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Windowing;

/// <summary>
/// Plans the labeling windows of plan §10.1.
///
/// <para>Two invariants make the merge in phase 3 trivial and the result reproducible:
/// <b>every line in a suspect region is committed by exactly one window</b>, and <b>no window
/// crosses a trusted boundary</b>. The first is what lets labels be merged by "take the label
/// from the window that commits this line" with no tie-breaking; the second keeps a model from
/// ever being in a position to contradict something the source already settled.</para>
/// </summary>
public sealed class WindowPlanner(PipelineOptions options)
{
    private readonly WindowingOptions _windowing = options.Windowing;

    /// <summary>
    /// Builds windows for one suspect region. <paramref name="allLines"/> is the full content-line
    /// list so overlap context can reach outside the region; only lines inside it are committed.
    /// </summary>
    public IReadOnlyList<LabelWindow> Plan(
        IReadOnlyList<LineRecord> allLines,
        Region region,
        IReadOnlySet<string> trustedBoundaryLineIds,
        int firstWindowIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(allLines);
        ArgumentNullException.ThrowIfNull(region);

        if (region.LineCount <= 0)
        {
            return [];
        }

        var windows = new List<LabelWindow>();
        var segments = SplitAtTrustedBoundaries(allLines, region, trustedBoundaryLineIds);

        foreach (var segment in segments)
        {
            foreach (var commit in ChunkCommitRegions(allLines, segment))
            {
                var overlap = (int)Math.Round(
                    (commit.EndExclusive - commit.Start) * _windowing.OverlapFraction,
                    MidpointRounding.AwayFromZero);

                // Context may reach outside the segment — it is only ever shown, never committed —
                // which is what gives the first and last window of a region real context too.
                var start = Math.Max(0, commit.Start - overlap);
                var end = Math.Min(allLines.Count, commit.EndExclusive + overlap);

                windows.Add(new LabelWindow(
                    WindowIndex: firstWindowIndex + windows.Count,
                    StartIndex: start,
                    EndIndexExclusive: end,
                    CommitStart: commit.Start,
                    CommitEndExclusive: commit.EndExclusive,
                    Lines: [.. allLines.Skip(start).Take(end - start)]));
            }
        }

        return windows;
    }

    /// <summary>Plans every suspect region in one pass, numbering windows continuously.</summary>
    public IReadOnlyList<LabelWindow> PlanAll(
        IReadOnlyList<LineRecord> allLines,
        IEnumerable<Region> suspectRegions,
        IReadOnlySet<string> trustedBoundaryLineIds)
    {
        var windows = new List<LabelWindow>();
        foreach (var region in suspectRegions)
        {
            windows.AddRange(Plan(allLines, region, trustedBoundaryLineIds, windows.Count));
        }

        return windows;
    }

    private static List<(int Start, int EndExclusive)> SplitAtTrustedBoundaries(
        IReadOnlyList<LineRecord> allLines,
        Region region,
        IReadOnlySet<string> trustedBoundaryLineIds)
    {
        var segments = new List<(int, int)>();
        var start = region.StartIndex;

        for (var i = region.StartIndex + 1; i < region.EndIndexExclusive; i++)
        {
            if (!trustedBoundaryLineIds.Contains(allLines[i].LineId))
            {
                continue;
            }

            segments.Add((start, i));
            start = i;
        }

        segments.Add((start, region.EndIndexExclusive));
        return segments;
    }

    /// <summary>
    /// Cuts a segment into commit regions no larger than the line or token budget. The chunks are
    /// balanced rather than greedy: a 210-line segment becomes two windows of 105, not one of 200
    /// and a runt of 10 whose context outweighs its content.
    /// </summary>
    private List<(int Start, int EndExclusive)> ChunkCommitRegions(
        IReadOnlyList<LineRecord> allLines,
        (int Start, int EndExclusive) segment)
    {
        var length = segment.EndExclusive - segment.Start;
        if (length <= 0)
        {
            return [];
        }

        var byTokens = MaxLinesForTokenBudget(allLines, segment);
        var maxLines = Math.Max(1, Math.Min(_windowing.MaxLines, byTokens));

        var chunkCount = (int)Math.Ceiling((double)length / maxLines);
        var chunkSize = (int)Math.Ceiling((double)length / chunkCount);

        var chunks = new List<(int, int)>(chunkCount);
        for (var start = segment.Start; start < segment.EndExclusive; start += chunkSize)
        {
            chunks.Add((start, Math.Min(segment.EndExclusive, start + chunkSize)));
        }

        return chunks;
    }

    /// <summary>
    /// How many of this segment's lines fit the input budget, from its own mean line length.
    /// A character ratio rather than a tokenizer: the planner runs before routing has chosen a
    /// model, so the exact tokenizer is not known yet, and the window only has to be roughly
    /// right — the adapter re-plans on a <c>ContextTooLong</c> error (plan §14.7).
    /// </summary>
    private int MaxLinesForTokenBudget(IReadOnlyList<LineRecord> allLines, (int Start, int EndExclusive) segment)
    {
        var length = segment.EndExclusive - segment.Start;
        var characters = 0L;
        for (var i = segment.Start; i < segment.EndExclusive; i++)
        {
            // Roughly the compact line format of plan §10.1: the text plus its feature columns.
            characters += allLines[i].Text.Length + 48;
        }

        var tokensPerLine = characters / Math.Max(1, length) / Math.Max(0.1, _windowing.CharsPerToken);
        return tokensPerLine <= 0 ? _windowing.MaxLines : Math.Max(1, (int)(_windowing.MaxInputTokens / tokensPerLine));
    }
}
