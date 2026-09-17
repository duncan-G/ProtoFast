using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;

namespace ProtoFast.Segmentation.Core.Validation;

/// <summary>
/// The deterministic checks of plan §11. Pure functions: no model, no I/O, no clock. They are the
/// fastest tests in the suite and the reason the pipeline can be trusted at all — every phase that
/// involves a model is followed by one of these, and a failure becomes a repair prompt rather
/// than a wrong answer that ships.
/// </summary>
public static class Checks
{
    public const string IdCoverage = "id-coverage";
    public const string LabelEnum = "label-enum";
    public const string TrustedRespect = "trusted-respect";
    public const string Contiguity = "contiguity";
    public const string TreeShape = "tree-shape";
    public const string HeadingAnchor = "heading-anchor";
    public const string SizeBounds = "size-bounds";
    public const string TextIntegrity = "text-integrity";
    public const string EditLineage = "edit-lineage";
    public const string Schema = "schema";
    public const string AugGrounding = "aug-grounding";

    /// <summary>
    /// Every expected id appears exactly once, no unknown ids, order preserved. Run against label
    /// output (expected = the window's committed lines) and against the tree (expected = every
    /// paragraph).
    /// </summary>
    public static ValidationResult CheckIdCoverage(
        IReadOnlyList<string> expectedIds,
        IReadOnlyList<string> actualIds,
        bool requireOrder = true)
    {
        var errors = new List<string>();
        var expected = new HashSet<string>(expectedIds, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in actualIds)
        {
            if (!expected.Contains(id))
            {
                errors.Add($"unknown id '{id}' — ids are issued by the pipeline and may not be invented");
            }
            else if (!seen.Add(id))
            {
                errors.Add($"duplicate id '{id}' — every id in scope must appear exactly once");
            }
        }

        foreach (var id in expectedIds.Where(id => !seen.Contains(id)))
        {
            errors.Add($"missing id '{id}'");
        }

        if (requireOrder && errors.Count == 0)
        {
            var order = expectedIds.Select((id, index) => (id, index))
                .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
            for (var i = 1; i < actualIds.Count; i++)
            {
                if (order[actualIds[i]] < order[actualIds[i - 1]])
                {
                    errors.Add($"'{actualIds[i]}' appears after '{actualIds[i - 1]}' but precedes it in the document");
                    break;
                }
            }
        }

        return ValidationResult.Fail(IdCoverage, errors);
    }

    /// <summary>Labels are valid enum values, and a HEAD has a level once phase 3b has run.</summary>
    public static ValidationResult CheckLabelEnum(
        IReadOnlyList<LineLabelResult> labels,
        bool requireHeadingLevels)
    {
        var errors = new List<string>();

        foreach (var label in labels)
        {
            if (!Enum.IsDefined(label.Label))
            {
                errors.Add($"{label.LineId}: '{label.Label}' is not a valid label");
            }

            if (!Enum.IsDefined(label.OtherKind))
            {
                errors.Add($"{label.LineId}: '{label.OtherKind}' is not a valid kind");
            }

            if (label.Confidence is < 0 or > 1)
            {
                errors.Add($"{label.LineId}: confidence {label.Confidence} is outside 0..1");
            }

            if (label.Label == LineLabel.Other && label.OtherKind == OtherKind.None)
            {
                errors.Add($"{label.LineId}: label OTHER requires a 'kind'");
            }

            if (requireHeadingLevels && label.Label == LineLabel.Head && label.HeadingLevel is not (> 0 and <= 6))
            {
                errors.Add($"{label.LineId}: heading has no level in 1..6");
            }
        }

        return ValidationResult.Fail(LabelEnum, errors);
    }

    /// <summary>
    /// No trusted boundary was removed and nothing merged across one. This is the check that keeps
    /// "trust existing structure" (plan §1) from being advice a model can ignore.
    /// </summary>
    public static ValidationResult CheckTrustedRespect(
        CleaningResult cleaning,
        IReadOnlyList<LineLabelResult> labels)
    {
        var errors = new List<string>();
        var byId = labels.ToDictionary(l => l.LineId, StringComparer.Ordinal);

        foreach (var boundary in cleaning.Boundaries.Where(b => b.IsImmovable))
        {
            if (!byId.TryGetValue(boundary.BeforeLineId, out var label))
            {
                errors.Add($"{boundary.BeforeLineId}: trusted {boundary.Kind} boundary has no label");
                continue;
            }

            var ok = boundary.Kind switch
            {
                BoundaryKind.Heading => label.Label == LineLabel.Head,
                _ => label.Label is LineLabel.Para or LineLabel.Head or LineLabel.Other,
            };

            if (!ok)
            {
                errors.Add(
                    $"{boundary.BeforeLineId}: trusted {boundary.Kind} boundary was labelled " +
                    $"{label.Label} — trusted boundaries cannot be removed");
            }
        }

        foreach (var lineId in cleaning.ContinuationLineIds)
        {
            if (byId.TryGetValue(lineId, out var label) && label.Label is LineLabel.Para or LineLabel.Head)
            {
                errors.Add(
                    $"{lineId}: cleaning established this line continues the previous one, " +
                    $"but it was labelled {label.Label}");
            }
        }

        return ValidationResult.Fail(TrustedRespect, errors);
    }

    /// <summary>Each section's paragraphs are contiguous in document order, and sections do not overlap.</summary>
    public static ValidationResult CheckContiguity(SectionNode root, IReadOnlyList<Paragraph> paragraphs)
    {
        var order = paragraphs
            .Select((p, index) => (p.ParagraphId, index))
            .ToDictionary(x => x.ParagraphId, x => x.index, StringComparer.Ordinal);

        var errors = new List<string>();
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in root.Descend())
        {
            foreach (var paragraphId in node.ParagraphIds)
            {
                if (claimed.TryGetValue(paragraphId, out var owner))
                {
                    errors.Add($"{paragraphId} appears in both {owner} and {node.SectionId}");
                }
                else
                {
                    claimed[paragraphId] = node.SectionId;
                }
            }

            var indices = node.ParagraphIds
                .Where(order.ContainsKey)
                .Select(id => order[id])
                .ToList();

            if (indices.Count > 1 && indices.Max() - indices.Min() != indices.Count - 1)
            {
                errors.Add(
                    $"{node.SectionId} ('{node.Title}'): paragraphs are not contiguous — " +
                    $"{node.ParagraphIds.Count} paragraphs spanning positions {indices.Min()}..{indices.Max()}");
            }
        }

        // The document's own reading order must also hold ACROSS sections: a depth-first walk of
        // the tree has to visit paragraphs in the same order the document does, or the tree is
        // describing a document that is not this one.
        var walkOrder = root.Descend()
            .SelectMany(n => n.ParagraphIds)
            .Where(order.ContainsKey)
            .Select(id => order[id])
            .ToList();

        for (var i = 1; i < walkOrder.Count; i++)
        {
            if (walkOrder[i] < walkOrder[i - 1])
            {
                errors.Add("tree walk visits paragraphs out of document order");
                break;
            }
        }

        return ValidationResult.Fail(Contiguity, errors);
    }

    /// <summary>
    /// Every node has children xor paragraphs; no empty sections; level equals depth. The
    /// children-xor-paragraphs rule is the plan's headline invariant (F9).
    /// </summary>
    public static ValidationResult CheckTreeShape(SectionNode root)
    {
        var errors = new List<string>();
        Walk(root, 1, errors);
        return ValidationResult.Fail(TreeShape, errors);

        static void Walk(SectionNode node, int depth, List<string> errors)
        {
            if (node.Children.Count > 0 && node.ParagraphIds.Count > 0)
            {
                errors.Add(
                    $"{node.SectionId} ('{node.Title}') has both {node.Children.Count} child sections and " +
                    $"{node.ParagraphIds.Count} paragraphs — a section contains one or the other, never both");
            }

            if (node.Children.Count == 0 && node.ParagraphIds.Count == 0)
            {
                errors.Add($"{node.SectionId} ('{node.Title}') is empty");
            }

            if (node.Level != depth)
            {
                errors.Add($"{node.SectionId} ('{node.Title}') has level {node.Level} at depth {depth}");
            }

            if (string.IsNullOrWhiteSpace(node.Title))
            {
                errors.Add($"{node.SectionId} has no title");
            }

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1, errors);
            }
        }
    }

    /// <summary>A node's heading anchor names a real HEAD line, and no line anchors two sections.</summary>
    public static ValidationResult CheckHeadingAnchor(
        SectionNode root,
        IReadOnlySet<string> headingLineIds)
    {
        var errors = new List<string>();
        var used = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in root.Descend().Where(n => n.HeadingLineId is not null))
        {
            var lineId = node.HeadingLineId!;

            if (!headingLineIds.Contains(lineId))
            {
                errors.Add($"{node.SectionId}: headingLineId '{lineId}' is not a line labelled HEAD");
            }

            if (!used.TryAdd(lineId, node.SectionId))
            {
                errors.Add($"heading '{lineId}' anchors both {used[lineId]} and {node.SectionId}");
            }

            if (node.TitleInferred)
            {
                errors.Add($"{node.SectionId} claims an inferred title but anchors source heading '{lineId}'");
            }
        }

        return ValidationResult.Fail(HeadingAnchor, errors);
    }

    /// <summary>Body paragraphs sit within the configured word range, unless explicitly waived.</summary>
    public static ValidationResult CheckSizeBounds(
        IReadOnlyList<Paragraph> paragraphs,
        ParagraphOptions options,
        IReadOnlySet<string>? waived = null)
    {
        var errors = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Kind != ParagraphKind.Body || waived?.Contains(paragraph.ParagraphId) == true)
            {
                continue;
            }

            if (paragraph.WordCount < options.MinWords)
            {
                errors.Add($"{paragraph.ParagraphId}: {paragraph.WordCount} words, below the minimum of {options.MinWords}");
            }
            else if (paragraph.WordCount > options.MaxWords)
            {
                errors.Add($"{paragraph.ParagraphId}: {paragraph.WordCount} words, above the maximum of {options.MaxWords}");
            }
        }

        return ValidationResult.Fail(SizeBounds, errors);
    }

    /// <summary>
    /// The hard gate (plan §11, N1). The frozen output's text must equal the cleaned source
    /// exactly. Normalization collapses whitespace and nothing else — no case folding, no
    /// punctuation, no Unicode changes — so a model that altered a single character fails here.
    ///
    /// <para><b>Headings count.</b> The plan's sketch compares paragraphs alone, but a heading's
    /// text is a cleaned source line too: assembly routes it to <see cref="Assembly.HeadingRecord"/>
    /// rather than into a paragraph, so a paragraphs-only comparison would fail on every document
    /// that has a heading — and, worse, would not notice a heading whose text had been altered.
    /// Both kinds of unit are reconstructed here, in document order.</para>
    /// </summary>
    public static ValidationResult CheckTextIntegrity(
        string integrityBaseline,
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<Assembly.HeadingRecord>? headings = null)
    {
        var actual = Ingest.TextMetrics.NormalizeWhitespace(Reconstruct(paragraphs, headings));
        var expected = Ingest.TextMetrics.NormalizeWhitespace(integrityBaseline);

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return ValidationResult.Pass(TextIntegrity);
        }

        var at = FirstDifference(expected, actual);
        return ValidationResult.Fail(
            TextIntegrity,
            $"Mismatch at char {at}: expected '{Excerpt(expected, at)}', got '{Excerpt(actual, at)}' " +
            $"(baseline {expected.Length} chars, paragraphs {actual.Length} chars)");
    }

    /// <summary>Every split/merge records its parents, and the lineage graph is acyclic.</summary>
    public static ValidationResult CheckEditLineage(IReadOnlyList<Paragraph> paragraphs)
    {
        var errors = new List<string>();
        var byId = paragraphs.ToDictionary(p => p.ParagraphId, StringComparer.Ordinal);

        foreach (var paragraph in paragraphs.Where(p => p.Lineage.Count > 0))
        {
            foreach (var parent in paragraph.Lineage)
            {
                if (parent == paragraph.ParagraphId)
                {
                    errors.Add($"{paragraph.ParagraphId}: lineage references itself");
                }
            }

            // A cycle would mean a repair produced a paragraph descended from its own descendant,
            // which makes "which text is authoritative?" unanswerable.
            var seen = new HashSet<string>(StringComparer.Ordinal) { paragraph.ParagraphId };
            var queue = new Queue<string>(paragraph.Lineage);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!seen.Add(id))
                {
                    errors.Add($"{paragraph.ParagraphId}: lineage contains a cycle through '{id}'");
                    break;
                }

                if (byId.TryGetValue(id, out var ancestor))
                {
                    foreach (var next in ancestor.Lineage)
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return ValidationResult.Fail(EditLineage, errors);
    }

    /// <summary>
    /// Runs the full post-structure suite of plan §9.8 in one call, so the freeze gate and the
    /// repair loop cannot drift apart about what "validated" means.
    /// </summary>
    public static ValidationReport CheckAll(
        CleaningResult cleaning,
        IReadOnlyList<LineLabelResult> labels,
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<Assembly.HeadingRecord> headings,
        SectionNode root,
        ParagraphOptions paragraphOptions,
        IReadOnlySet<string>? waivedSizeParagraphIds = null)
    {
        var headingLineIds = labels
            .Where(l => l.Label == LineLabel.Head)
            .Select(l => l.LineId)
            .ToHashSet(StringComparer.Ordinal);

        return new ValidationReport(
        [
            CheckLabelEnum(labels, requireHeadingLevels: true),
            CheckTrustedRespect(cleaning, labels),
            CheckIdCoverage(
                [.. paragraphs.Select(p => p.ParagraphId)],
                [.. root.Descend().SelectMany(n => n.ParagraphIds)]),
            CheckTreeShape(root),
            CheckHeadingAnchor(root, headingLineIds),
            CheckContiguity(root, paragraphs),
            CheckSizeBounds(paragraphs, paragraphOptions, waivedSizeParagraphIds),
            CheckEditLineage(paragraphs),
            CheckTextIntegrity(cleaning.IntegrityBaseline, paragraphs, headings),
        ]);
    }

    /// <summary>
    /// Paragraph and heading texts back in document order. Both anchor to a line id, and line ids
    /// are zero-padded and monotonic (plan §8.5), so ordinal comparison on them <em>is</em>
    /// document order — no second walk of the document needed.
    /// </summary>
    internal static string Reconstruct(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<Assembly.HeadingRecord>? headings)
    {
        var units = paragraphs
            .Select(p => (Key: p.FirstLineId, p.Text))
            .Concat((headings ?? []).Select(h => (Key: h.LineId, h.Text)))
            .OrderBy(u => u.Key, StringComparer.Ordinal);

        return string.Join(' ', units.Select(u => u.Text));
    }

    internal static int FirstDifference(string expected, string actual)
    {
        var limit = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < limit; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return limit;
    }

    internal static string Excerpt(string value, int at, int radius = 40)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var start = Math.Max(0, at - radius);
        var length = Math.Min(value.Length - start, radius * 2);
        return value.Substring(start, length);
    }
}
