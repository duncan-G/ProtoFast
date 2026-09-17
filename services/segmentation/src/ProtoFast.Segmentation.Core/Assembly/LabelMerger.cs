using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Assembly;

/// <summary>
/// Merges per-window label results into one label per line (plan §9.5).
///
/// <para>The precedence is the whole point: trusted boundaries and deterministic cleaning
/// decisions beat anything a model said, and among model outputs a line's label comes from the
/// window that <em>commits</em> it — never from a window that merely saw it as context. That
/// makes the merge a total function with no tie-breaking and no dependence on the order windows
/// happened to complete in, which is what makes a re-run reproducible.</para>
/// </summary>
public static class LabelMerger
{
    public static IReadOnlyList<LineLabelResult> Merge(
        IReadOnlyList<LineRecord> contentLines,
        CleaningResult cleaning,
        IReadOnlyDictionary<int, IReadOnlyList<LineLabelResult>> windowResults,
        IReadOnlyList<LabelWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(contentLines);
        ArgumentNullException.ThrowIfNull(cleaning);

        var committedBy = BuildCommitMap(windows, windowResults);
        var boundaries = cleaning.Boundaries.ToDictionary(b => b.BeforeLineId, StringComparer.Ordinal);

        var merged = new List<LineLabelResult>(contentLines.Count);
        foreach (var line in contentLines)
        {
            merged.Add(ResolveLabel(line, cleaning, boundaries, committedBy));
        }

        return merged;
    }

    private static LineLabelResult ResolveLabel(
        LineRecord line,
        CleaningResult cleaning,
        IReadOnlyDictionary<string, Boundary> boundaries,
        IReadOnlyDictionary<string, LineLabelResult> committedBy)
    {
        // 1. A trusted boundary settles the line outright; no model output can move it.
        if (boundaries.TryGetValue(line.LineId, out var boundary) && boundary.IsImmovable)
        {
            return boundary.Kind == BoundaryKind.Heading
                ? new LineLabelResult(line.LineId, LineLabel.Head, line.HeadingLevelHint, 1.0)
                : new LineLabelResult(line.LineId, LineLabel.Para, null, 1.0, KindFor(cleaning, line.LineId));
        }

        // 2. Cleaning proved this line continues the previous one (hyphen join, page break).
        if (cleaning.ContinuationLineIds.Contains(line.LineId))
        {
            return new LineLabelResult(line.LineId, LineLabel.Cont, null, 1.0, KindFor(cleaning, line.LineId));
        }

        // 3. Otherwise the committing window's answer, if there was one.
        if (committedBy.TryGetValue(line.LineId, out var labelled))
        {
            return labelled;
        }

        // 4. No model looked at it and nothing was trusted: it continues the paragraph. Every line
        //    in a non-suspect block that is not a boundary is, by construction, a continuation.
        return new LineLabelResult(line.LineId, LineLabel.Cont, null, 1.0, KindFor(cleaning, line.LineId));
    }

    private static Dictionary<string, LineLabelResult> BuildCommitMap(
        IReadOnlyList<LabelWindow> windows,
        IReadOnlyDictionary<int, IReadOnlyList<LineLabelResult>> windowResults)
    {
        var map = new Dictionary<string, LineLabelResult>(StringComparer.Ordinal);

        foreach (var window in windows)
        {
            if (!windowResults.TryGetValue(window.WindowIndex, out var results))
            {
                continue;
            }

            var committed = window.Lines
                .Select((line, offset) => (line, index: window.StartIndex + offset))
                .Where(x => window.Commits(x.index))
                .Select(x => x.line.LineId)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var result in results.Where(r => committed.Contains(r.LineId)))
            {
                map[result.LineId] = result;
            }
        }

        return map;
    }

    private static OtherKind KindFor(CleaningResult cleaning, string lineId) =>
        cleaning.OtherLineKinds.TryGetValue(lineId, out var kind) ? kind : OtherKind.None;
}
