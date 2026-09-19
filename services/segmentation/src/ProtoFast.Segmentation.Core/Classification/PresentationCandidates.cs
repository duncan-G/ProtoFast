using System.Text.RegularExpressions;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Classification;

/// <summary>
/// One paragraph offered to the phase-5 classifier, with the reason it was offered.
///
/// <para><see cref="Suggested"/> is what code already believes. The agent sees candidates only
/// (scene plan §8.5 step 3) and may disagree; a candidate with no agent answer falls back to this,
/// which is what keeps the phase cheap on a document whose front matter is unambiguous.</para>
/// </summary>
public sealed record PresentationCandidate(
    string ParagraphId,
    int Index,
    MetadataClass? Suggested,
    IReadOnlyList<string> Reasons,
    double Confidence);

/// <summary>
/// The deterministic half of phase 5 (scene plan §8.5 steps 1 and 4).
///
/// <para>Two properties of the document are what make this phase a fan-out rather than an
/// orchestration (§8.2): <b>front matter is a prefix and back matter is a suffix</b>, and every
/// other class is local to a single paragraph. Both are guarantees a window planner can impose, so
/// the cross-window join needs no judgement and code performs it.</para>
///
/// <para>Everything here proposes; nothing here decides. The default on uncertainty is
/// <see cref="Presentation.Displayable"/> and the asymmetry driving it is §5.2's:
/// <b>under-removal is recoverable in review, over-removal silently loses the work.</b></para>
/// </summary>
public static partial class PresentationCandidates
{
    /// <summary>
    /// Offers the paragraphs worth a model call. A document whose edges are plainly body text
    /// yields none, and the phase then costs nothing at all.
    /// </summary>
    public static IReadOnlyList<PresentationCandidate> Propose(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlySet<string> artifactParagraphIds,
        PresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(artifactParagraphIds);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new Dictionary<string, PresentationCandidate>(StringComparer.Ordinal);
        var edge = Math.Max(0, options.EdgeParagraphs);

        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var reasons = new List<string>();
            MetadataClass? suggested = null;
            var confidence = 0.5;

            // Phase 1 already decided these are not text. Production noise is the one class the
            // cleaner is authoritative about, so it arrives with the cleaner's confidence.
            if (artifactParagraphIds.Contains(paragraph.ParagraphId))
            {
                reasons.Add("cleaning labelled this line an artifact");
                suggested = MetadataClass.ProductionNoise;
                confidence = 0.9;
            }

            // Position is a filter, not a reason on its own. Front matter is short — a title, an
            // author, a copyright line — and body prose in the first forty paragraphs is body prose.
            // Offering every edge paragraph would make the phase cost a model call on every document
            // and buy nothing on the ones whose edges are plainly text.
            if (i < edge && IsEdgeShaped(paragraph))
            {
                var signature = FrontMatterSignature(paragraph.Text);

                if (signature is not null || IsPlausibleFrontMatter(paragraph, i))
                {
                    reasons.Add($"paragraph {i + 1} of the document, {paragraph.WordCount} words");
                    suggested ??= signature;
                }
            }
            else if (i >= paragraphs.Count - edge && IsEdgeShaped(paragraph))
            {
                var signature = BackMatterSignature(paragraph.Text);

                if (signature is not null)
                {
                    reasons.Add($"paragraph {paragraphs.Count - i} from the end");
                    suggested ??= signature;
                }
            }

            if (LooksLikeTableOfContents(paragraph.Text))
            {
                reasons.Add("dot leaders or trailing page numbers on most lines");
                suggested = MetadataClass.FrontMatter;
                confidence = Math.Max(confidence, 0.8);
            }

            if (IsBareNavigationalLabel(paragraph.Text))
            {
                reasons.Add("a bare numbering label with no title after it");
                suggested = MetadataClass.NavigationalLabel;
                confidence = Math.Max(confidence, 0.75);
            }

            if (reasons.Count > 0)
            {
                candidates[paragraph.ParagraphId] = new PresentationCandidate(
                    paragraph.ParagraphId, i, suggested, reasons, confidence);
            }
        }

        return [.. candidates.Values.OrderBy(c => c.Index)];
    }

    /// <summary>
    /// Applies the agent's answers over the candidates and defaults everything else to displayable
    /// (§8.5 step 4). A candidate the agent did not answer for keeps <see cref="PresentationCandidate.Suggested"/>,
    /// and a suggestion of null means "code noticed it, code has no opinion" — which resolves to
    /// displayable, as it must.
    /// </summary>
    public static IReadOnlyList<ParagraphPresentation> Resolve(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<PresentationCandidate> candidates,
        IReadOnlyDictionary<string, ParagraphPresentation> answers)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(answers);

        var byId = candidates.ToDictionary(c => c.ParagraphId, StringComparer.Ordinal);
        var resolved = new List<ParagraphPresentation>(paragraphs.Count);

        foreach (var paragraph in paragraphs)
        {
            if (answers.TryGetValue(paragraph.ParagraphId, out var answer))
            {
                // A metadata verdict with no class is not a verdict. Rather than invent one, the
                // paragraph stays displayable — over-removal is the expensive mistake (§5.2).
                resolved.Add(answer is { Presentation: Presentation.Metadata, Class: null }
                    ? ParagraphPresentation.Displayable(paragraph.ParagraphId, answer.Confidence)
                    : answer);
                continue;
            }

            if (byId.TryGetValue(paragraph.ParagraphId, out var candidate) && candidate.Suggested is { } suggested)
            {
                resolved.Add(ParagraphPresentation.Metadata(
                    paragraph.ParagraphId, suggested, [paragraph.ParagraphId], candidate.Confidence));
                continue;
            }

            resolved.Add(ParagraphPresentation.Displayable(paragraph.ParagraphId));
        }

        return resolved;
    }

    /// <summary>
    /// Front matter is a <em>prefix</em> and back matter a <em>suffix</em> (§8.2). A classifier
    /// that put one in the middle of the document has produced an answer the document's shape
    /// rules out, so the run is taken back to the nearest contiguous reading rather than stored.
    ///
    /// <para>This is the join the window planner guarantees, performed in code — the reason phase 5
    /// never needed an orchestrator.</para>
    /// </summary>
    public static IReadOnlyList<ParagraphPresentation> EnforceEdges(
        IReadOnlyList<ParagraphPresentation> presentations)
    {
        ArgumentNullException.ThrowIfNull(presentations);

        var result = presentations.ToArray();

        var frontEnd = 0;
        while (frontEnd < result.Length && IsEdgeClass(result[frontEnd], MetadataClass.FrontMatter))
        {
            frontEnd++;
        }

        var backStart = result.Length;
        while (backStart > frontEnd && IsEdgeClass(result[backStart - 1], MetadataClass.BackMatter))
        {
            backStart--;
        }

        for (var i = frontEnd; i < backStart; i++)
        {
            if (result[i].Class is MetadataClass.FrontMatter or MetadataClass.BackMatter)
            {
                result[i] = ParagraphPresentation.Displayable(result[i].ParagraphId, result[i].Confidence);
            }
        }

        return result;
    }

    /// <summary>
    /// A paragraph interleaved in the prefix run still belongs to it: a title page holding one line
    /// of an epigraph does not end the front matter. Only a displayable paragraph that is not
    /// itself edge metadata closes the run.
    /// </summary>
    private static bool IsEdgeClass(ParagraphPresentation presentation, MetadataClass edge) =>
        presentation.Class == edge
        || presentation.Class is MetadataClass.ProductionNoise or MetadataClass.RunningApparatus
            or MetadataClass.NavigationalLabel;

    /// <summary>
    /// Short enough to be apparatus. A title page, a copyright line and a ToC entry are all brief;
    /// a paragraph of prose is not, whatever it sits next to.
    /// </summary>
    private static bool IsEdgeShaped(Paragraph paragraph) =>
        paragraph.WordCount <= MaxApparatusWords || LooksLikeTableOfContents(paragraph.Text);

    /// <summary>
    /// A short paragraph in the opening block, with no sentence punctuation — the shape of a title,
    /// a byline or a series line. It is offered <em>without</em> a suggested class, because code has
    /// noticed something and has no opinion about it; the classifier decides and the default is
    /// displayable.
    /// </summary>
    private static bool IsPlausibleFrontMatter(Paragraph paragraph, int index) =>
        index < OpeningBlockParagraphs
        && paragraph.WordCount <= MaxApparatusWords
        && !paragraph.Text.TrimEnd().EndsWith('.');

    /// <summary>Apparatus is short. Above this a paragraph is prose, wherever it sits.</summary>
    private const int MaxApparatusWords = 25;

    /// <summary>
    /// How far into the document a bare short line is still plausibly a title page. Tighter than
    /// <c>EdgeParagraphs</c>, which bounds where a <em>signature</em> is looked for.
    /// </summary>
    private const int OpeningBlockParagraphs = 8;

    private static MetadataClass? FrontMatterSignature(string text)
    {
        var trimmed = text.Trim();

        if (CopyrightPattern().IsMatch(trimmed) || IsbnPattern().IsMatch(trimmed))
        {
            return MetadataClass.FrontMatter;
        }

        return FrontMatterHeadingPattern().IsMatch(trimmed) ? MetadataClass.FrontMatter : null;
    }

    private static MetadataClass? BackMatterSignature(string text) =>
        BackMatterHeadingPattern().IsMatch(text.Trim()) ? MetadataClass.BackMatter : null;

    /// <summary>
    /// Dot leaders, or a majority of lines ending in a bare page number. Both are typographic facts
    /// about a table of contents rather than claims about its words, which is what lets this run
    /// before anything has read the document.
    /// </summary>
    internal static bool LooksLikeTableOfContents(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var signals = lines.Count(line => DotLeaderPattern().IsMatch(line) || TrailingPageNumberPattern().IsMatch(line));
        return signals * 2 >= lines.Length && signals >= 2;
    }

    /// <summary>
    /// "Chapter 4" or "3.2" standing alone — numbering separated from the title it numbers. A label
    /// carrying its own title is a heading and is not this.
    /// </summary>
    internal static bool IsBareNavigationalLabel(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 24
            && TextMetrics.WordCount(trimmed) <= 3
            && BareLabelPattern().IsMatch(trimmed);
    }

    [GeneratedRegex(@"©|\bcopyright\b|\ball rights reserved\b", RegexOptions.IgnoreCase)]
    private static partial Regex CopyrightPattern();

    [GeneratedRegex(@"\bISBN\b|\bISSN\b|\bDOI\b", RegexOptions.IgnoreCase)]
    private static partial Regex IsbnPattern();

    [GeneratedRegex(
        @"^(table of contents|contents|list of (figures|tables|illustrations)|dedication|epigraph|"
        + @"frontispiece|half[- ]title|title page|preface|acknowledge?ments?|foreword)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex FrontMatterHeadingPattern();

    [GeneratedRegex(
        @"^(index|colophon|about the author|about the publisher|advertisement|"
        + @"also by (the same author|this author)|credits)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex BackMatterHeadingPattern();

    [GeneratedRegex(@"\.{4,}\s*\d+\s*$")]
    private static partial Regex DotLeaderPattern();

    [GeneratedRegex(@"\S\s{2,}\d{1,4}\s*$")]
    private static partial Regex TrailingPageNumberPattern();

    [GeneratedRegex(
        @"^((chapter|section|part|appendix|book|unit|lesson|exercise|figure|table|plate)\s+)?"
        + @"([0-9]+([.:][0-9]+)*|[IVXLCDM]+|[A-Z])\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BareLabelPattern();
}
