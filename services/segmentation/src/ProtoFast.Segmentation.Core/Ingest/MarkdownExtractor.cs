using System.Text.RegularExpressions;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>Phase 0's output: lines, statistics and the detected family (plan §9.2).</summary>
public sealed record ExtractionResult(
    IReadOnlyList<LineRecord> Lines,
    DocumentStatistics Statistics,
    string DocumentFamily);

/// <summary>
/// Turns uploaded Markdown into <see cref="LineRecord"/>s, one per visual line, preserving what
/// the converter claimed about each (<c>#</c> headings, blank lines, list markers, fences,
/// tables).
///
/// <para>It is deliberately a line scanner rather than a Markdig AST walk. The input is
/// converter output, not authored Markdown: it is frequently malformed, and the pipeline's unit
/// is the visual line — which an AST erases. What the converter claimed becomes a
/// <see cref="SourceHint"/>, and phase 1 decides how far to trust it.</para>
/// </summary>
public static partial class MarkdownExtractor
{
    public static ExtractionResult Extract(string markdown, LayoutDocument? layout = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var rawLines = SplitLines(markdown);
        var rawLayout = LayoutJoiner.Join(rawLines, layout);

        // (text, hint, headingLevel, index into rawLines) for every line that survives to phase 1.
        var emitted = new List<(string Text, SourceHint Hint, int? Level, int RawIndex)>(rawLines.Count);

        var inFence = false;
        var blankBefore = true;

        for (var i = 0; i < rawLines.Count; i++)
        {
            var text = rawLines[i];

            if (string.IsNullOrWhiteSpace(text) && !inFence)
            {
                // Blank lines are structure, not content: they become the next line's hint and are
                // never emitted, so paragraph text never carries an empty line.
                blankBefore = true;
                continue;
            }

            var trimmed = text.TrimStart();

            if (FencePattern().IsMatch(trimmed))
            {
                inFence = !inFence;
                emitted.Add((text.TrimEnd(), SourceHint.CodeFence, null, i));
                blankBefore = false;
                continue;
            }

            if (inFence)
            {
                emitted.Add((text.TrimEnd(), SourceHint.CodeFence, null, i));
                blankBefore = false;
                continue;
            }

            var headingMatch = AtxHeadingPattern().Match(trimmed);
            if (headingMatch.Success)
            {
                emitted.Add((
                    headingMatch.Groups["text"].Value.Trim().TrimEnd('#').TrimEnd(),
                    SourceHint.MarkdownHeading,
                    headingMatch.Groups["hashes"].Value.Length,
                    i));
                blankBefore = false;
                continue;
            }

            // A setext underline (=== / ---) retitles the PREVIOUS line rather than being content.
            var setextLevel = SetextLevel(trimmed);
            if (setextLevel is not null
                && emitted.Count > 0
                && emitted[^1].Hint is SourceHint.None or SourceHint.BlankLineBefore)
            {
                var previous = emitted[^1];
                emitted[^1] = (previous.Text, SourceHint.MarkdownHeading, setextLevel, previous.RawIndex);
                blankBefore = false;
                continue;
            }

            var hint =
                TableRowPattern().IsMatch(trimmed) ? SourceHint.TableRow
                : ListItemPattern().IsMatch(trimmed) ? SourceHint.ListItem
                : BlockQuotePattern().IsMatch(trimmed) ? SourceHint.Quote
                : blankBefore ? SourceHint.BlankLineBefore
                : SourceHint.None;

            emitted.Add((text.TrimEnd(), hint, null, i));
            blankBefore = false;
        }

        var lineLayout = emitted.Select(e => rawLayout[e.RawIndex]).ToList();
        var statistics = DocumentStatisticsCalculator.Compute(
            [.. emitted.Select(e => e.Text)], lineLayout, rawLines);

        var lines = new List<LineRecord>(emitted.Count);
        for (var i = 0; i < emitted.Count; i++)
        {
            var (text, hint, level, _) = emitted[i];
            lines.Add(new LineRecord(
                Ids.Line(i),
                text,
                DocumentStatisticsCalculator.Normalize(lineLayout[i], lineLayout, i, statistics),
                hint,
                level));
        }

        return new ExtractionResult(lines, statistics, FamilyDetector.Detect(lines, statistics, layout));
    }

    internal static List<string> SplitLines(string markdown) =>
        [.. markdown.ReplaceLineEndings("\n").Split('\n')];

    private static int? SetextLevel(string trimmed) =>
        SetextH1Pattern().IsMatch(trimmed) ? 1
        : SetextH2Pattern().IsMatch(trimmed) ? 2
        : null;

    [GeneratedRegex(@"^(?<hashes>#{1,6})\s+(?<text>.*)$")]
    private static partial Regex AtxHeadingPattern();

    [GeneratedRegex(@"^(`{3,}|~{3,})")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^\|.*\|\s*$")]
    private static partial Regex TableRowPattern();

    [GeneratedRegex(@"^([-*+]\s+|\d+[.)]\s+)")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^>\s?")]
    private static partial Regex BlockQuotePattern();

    [GeneratedRegex(@"^={2,}\s*$")]
    private static partial Regex SetextH1Pattern();

    [GeneratedRegex(@"^-{2,}\s*$")]
    private static partial Regex SetextH2Pattern();
}
