using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Cleaning;

/// <summary>
/// Phase 1: the deterministic rules of plan §9.3, in order. Every step records a
/// <see cref="CleaningEdit"/> so review can show exactly what code did and undo it.
///
/// <para>Nothing here asks a model anything. That is the point of the phase: the cheaper and
/// more of the document this handles, the fewer suspect regions triage produces and the less the
/// run costs — a clean Markdown upload should reach phase 4 without a single provider call.</para>
/// </summary>
public sealed class DocumentCleaner(PipelineOptions options)
{
    private readonly CleaningOptions _cleaning = options.Cleaning;

    public CleaningResult Clean(IReadOnlyList<LineRecord> lines, DocumentStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var edits = new List<CleaningEdit>();
        var artifacts = new HashSet<string>(StringComparer.Ordinal);
        var otherKinds = new Dictionary<string, OtherKind>(StringComparer.Ordinal);
        var working = lines.ToList();

        MarkRunningHeadersAndFooters(working, statistics, artifacts, edits);
        MarkPageNumbers(working, artifacts, edits);
        MarkOpaqueBlocks(working, otherKinds);

        var hyphenJoined = RejoinHyphenation(working, artifacts, edits);
        var continuations = new HashSet<string>(hyphenJoined, StringComparer.Ordinal);
        MarkPageBreakContinuations(working, artifacts, statistics, continuations);

        var boundaries = FindTrustedBoundaries(working, artifacts, continuations, statistics);

        return new CleaningResult(
            working, boundaries, artifacts, otherKinds, continuations, hyphenJoined, edits);
    }

    /// <summary>
    /// A line whose digit-masked text repeats in the margin zone on enough pages is chrome, not
    /// content. Masking digits is what lets "Page 12" and "Page 340" count as the same header.
    /// </summary>
    private void MarkRunningHeadersAndFooters(
        List<LineRecord> lines,
        DocumentStatistics statistics,
        HashSet<string> artifacts,
        List<CleaningEdit> edits)
    {
        if (!statistics.HasLayout || statistics.PageCount < 3)
        {
            return;
        }

        var zone = _cleaning.MarginZoneFraction;
        var candidates = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (line.Layout is not { } layout || (layout.Top > zone && layout.Top < 1 - zone))
            {
                continue;
            }

            var key = TextMetrics.MaskDigits(line.Text);
            if (key.Length == 0)
            {
                continue;
            }

            if (!candidates.TryGetValue(key, out var pages))
            {
                candidates[key] = pages = [];
            }

            pages.Add(layout.Page);
        }

        var threshold = Math.Max(2, (int)Math.Ceiling(statistics.PageCount * _cleaning.RunningHeaderPageFraction));
        var repeating = candidates
            .Where(kv => kv.Value.Count >= threshold)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (repeating.Count == 0)
        {
            return;
        }

        foreach (var line in lines)
        {
            if (line.Layout is not { } layout || (layout.Top > zone && layout.Top < 1 - zone))
            {
                continue;
            }

            if (repeating.Contains(TextMetrics.MaskDigits(line.Text)) && artifacts.Add(line.LineId))
            {
                edits.Add(new CleaningEdit(line.LineId, "running-header-footer", line.Text, string.Empty));
            }
        }
    }

    private void MarkPageNumbers(List<LineRecord> lines, HashSet<string> artifacts, List<CleaningEdit> edits)
    {
        var zone = _cleaning.MarginZoneFraction;
        foreach (var line in lines)
        {
            if (artifacts.Contains(line.LineId) || !TextMetrics.LooksLikePageNumber(line.Text))
            {
                continue;
            }

            // Without layout, a bare number on its own line is still almost certainly a page
            // number — but a numbered list item is not, and the extractor already hinted that.
            var inMarginZone = line.Layout is null
                ? line.Hint is not SourceHint.ListItem and not SourceHint.TableRow
                : line.Layout.Top <= zone || line.Layout.Top >= 1 - zone;

            if (inMarginZone && artifacts.Add(line.LineId))
            {
                edits.Add(new CleaningEdit(line.LineId, "page-number", line.Text, string.Empty));
            }
        }
    }

    /// <summary>Fences, table rows, list items and quotes stay whole and opaque (plan §9.3).</summary>
    private static void MarkOpaqueBlocks(List<LineRecord> lines, Dictionary<string, OtherKind> otherKinds)
    {
        foreach (var line in lines)
        {
            var kind = line.Hint switch
            {
                SourceHint.CodeFence => OtherKind.Code,
                SourceHint.TableRow => OtherKind.Table,
                SourceHint.ListItem => OtherKind.ListItem,
                SourceHint.Quote => OtherKind.Quote,
                _ => OtherKind.None,
            };

            if (kind != OtherKind.None)
            {
                otherKinds[line.LineId] = kind;
            }
        }
    }

    /// <summary>
    /// Joins a word the converter broke across a line end.
    ///
    /// <para>The hard case is telling <c>instru-/ment</c> (hyphenation to fix) from
    /// <c>well-/known</c> (a real compound whose hyphen must survive). With no bundled
    /// dictionary the document is the only evidence available, and it answers in two ways: the
    /// joined form appearing elsewhere is direct proof, and the leading fragment <em>never</em>
    /// appearing as a word of its own is strong indirect proof — "instru" is not a word this
    /// document uses, "well" is. Neither test fires for an unseen compound, so the hyphen is kept
    /// and the line is still marked a continuation. That is the safe direction to be wrong in:
    /// the paragraph comes out right either way, and only the spelling of one word is at stake.</para>
    /// </summary>
    private static HashSet<string> RejoinHyphenation(
        List<LineRecord> lines,
        HashSet<string> artifacts,
        List<CleaningEdit> edits)
    {
        var vocabulary = BuildVocabulary(lines);
        var continuations = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Count - 1; i++)
        {
            var current = lines[i];
            if (artifacts.Contains(current.LineId) || !TextMetrics.EndsWithHyphen(current.Text))
            {
                continue;
            }

            var next = NextContentLine(lines, artifacts, i);
            if (next is null || !TextMetrics.StartsLowercase(next.Value.Line.Text))
            {
                continue;
            }

            var head = current.Text.TrimEnd();
            var stem = head[..^1];
            var lastWord = stem[(stem.LastIndexOfAny([' ', '\t']) + 1)..].ToLowerInvariant();
            var nextWord = next.Value.Line.Text.TrimStart()
                .Split([' ', '\t', ',', '.', ';', ':'], 2)[0]
                .Trim('-', '‐', '­')
                .ToLowerInvariant();
            var candidate = (lastWord + nextWord).Trim();

            if (candidate.Length == 0)
            {
                continue;
            }

            if (!vocabulary.Contains(candidate) && vocabulary.Contains(lastWord))
            {
                // Looks like a real compound. Keep the hyphen — but the next line still continues
                // this paragraph, because pagination broke the line, not the sentence.
                continuations.Add(next.Value.Line.LineId);
                continue;
            }

            // The join is recorded on the line that KEEPS the text: the hyphen disappears and the
            // next line becomes a continuation, so assembly glues them with no space.
            lines[i] = current with { Text = stem };
            edits.Add(new CleaningEdit(current.LineId, "hyphen-join", current.Text, stem));
            continuations.Add(next.Value.Line.LineId);
        }

        return continuations;
    }

    /// <summary>
    /// A wide, unpunctuated last line of a page whose successor reads like mid-sentence is one
    /// paragraph split by pagination — the single most common artifact of PDF conversion.
    /// </summary>
    private void MarkPageBreakContinuations(
        List<LineRecord> lines,
        HashSet<string> artifacts,
        DocumentStatistics statistics,
        HashSet<string> continuations)
    {
        if (!statistics.HasLayout)
        {
            return;
        }

        for (var i = 0; i < lines.Count - 1; i++)
        {
            var current = lines[i];
            if (artifacts.Contains(current.LineId) || current.Layout is not { } layout)
            {
                continue;
            }

            var next = NextContentLine(lines, artifacts, i);
            if (next is null || next.Value.Line.Layout is not { } nextLayout || nextLayout.Page == layout.Page)
            {
                continue;
            }

            var endsOpen = layout.Width >= _cleaning.FullWidthThreshold
                && !TextMetrics.EndsWithTerminalPunctuation(current.Text);
            var startsOpen = TextMetrics.StartsLowercase(next.Value.Line.Text)
                || nextLayout.Indent <= _cleaning.ParagraphIndent;

            if (endsOpen && startsOpen)
            {
                continuations.Add(next.Value.Line.LineId);
            }
        }
    }

    /// <summary>
    /// The boundaries later phases may not remove (plan §9.3). A converter heading hint that
    /// contradicts layout — a <c>#</c> on a full-width body-size line — is deliberately NOT
    /// trusted; it becomes a hint for the labeler instead, which is what keeps a bad converter
    /// from dictating the tree.
    /// </summary>
    private List<Boundary> FindTrustedBoundaries(
        List<LineRecord> lines,
        HashSet<string> artifacts,
        HashSet<string> continuations,
        DocumentStatistics statistics)
    {
        var boundaries = new List<Boundary>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (artifacts.Contains(line.LineId))
            {
                continue;
            }

            if (IsTrustedHeading(line, statistics))
            {
                boundaries.Add(new Boundary(line.LineId, BoundaryKind.Heading, BoundarySource.Trusted, 1.0));
                continue;
            }

            if (continuations.Contains(line.LineId))
            {
                // Deterministically established as mid-paragraph; no boundary, and the labeler is
                // told so rather than being asked.
                continue;
            }

            if (i > 0 && IsTrustedParagraphBreak(line))
            {
                boundaries.Add(new Boundary(line.LineId, BoundaryKind.ParagraphStart, BoundarySource.Trusted, 1.0));
            }
        }

        // The document always starts a paragraph, whatever the source said.
        var first = lines.FirstOrDefault(l => !artifacts.Contains(l.LineId));
        if (first is not null && !boundaries.Any(b => b.BeforeLineId == first.LineId))
        {
            boundaries.Insert(0, new Boundary(first.LineId, BoundaryKind.ParagraphStart, BoundarySource.Trusted, 1.0));
        }

        return boundaries;
    }

    private bool IsTrustedHeading(LineRecord line, DocumentStatistics statistics)
    {
        if (line.Hint != SourceHint.MarkdownHeading)
        {
            return false;
        }

        if (line.Layout is not { } layout)
        {
            // No layout to corroborate with: a numbering pattern is the only independent
            // evidence available, and a short title-cased line is the weaker fallback.
            return !statistics.HasLayout
                && (TextMetrics.HasNumberingPrefix(line.Text) || TextMetrics.WordCount(line.Text) <= 12);
        }

        return layout.FontScale >= _cleaning.HeadingFontScale
            || (layout.IsBold && layout.GapAbove >= _cleaning.HeadingGapAbove);
    }

    private bool IsTrustedParagraphBreak(LineRecord line)
    {
        if (line.Hint is not (SourceHint.BlankLineBefore or SourceHint.ListItem or SourceHint.TableRow or SourceHint.CodeFence))
        {
            return false;
        }

        if (line.Layout is not { } layout)
        {
            return true;
        }

        return layout.GapAbove >= _cleaning.ParagraphGapAbove || layout.Indent > _cleaning.ParagraphIndent;
    }

    private static (LineRecord Line, int Index)? NextContentLine(
        List<LineRecord> lines, HashSet<string> artifacts, int from)
    {
        for (var i = from + 1; i < lines.Count; i++)
        {
            if (!artifacts.Contains(lines[i].LineId))
            {
                return (lines[i], i);
            }
        }

        return null;
    }

    /// <summary>
    /// Every word the document uses, lowercased. The fragment before a line-ending hyphen is
    /// deliberately excluded: counting "instru-" as evidence that "instru" is a word would let
    /// the compound test above answer its own question.
    /// </summary>
    private static HashSet<string> BuildVocabulary(List<LineRecord> lines)
    {
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var text = line.Text;
            if (TextMetrics.EndsWithHyphen(text))
            {
                var stem = text.TrimEnd()[..^1];
                var separator = stem.LastIndexOfAny([' ', '\t']);
                text = separator < 0 ? string.Empty : stem[..separator];
            }

            foreach (var word in text.Split(
                [' ', '\t', ',', '.', ';', ':', '(', ')', '"'], StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = word.Trim('-', '‐', '­').ToLowerInvariant();
                if (cleaned.Length > 1)
                {
                    vocabulary.Add(cleaned);
                }
            }
        }

        return vocabulary;
    }
}
