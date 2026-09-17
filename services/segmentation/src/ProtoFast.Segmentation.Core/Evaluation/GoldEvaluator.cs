using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Evaluation;

/// <summary>What a pipeline run produced, in the shape evaluation needs (no I/O dependencies).</summary>
public sealed record EvaluatedRun(
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<HeadingRecord> Headings,
    SectionNode Root,
    IReadOnlySet<string> ArtifactTexts,
    ValidationReport Validation,
    decimal CostUsd,
    TimeSpan Duration);

/// <summary>
/// Scores one run against one gold document (plan §26.2).
///
/// <para>Boundaries are compared over <em>words</em>, not over lines. Line ids depend on how the
/// converter broke the page, which differs between the gold annotation and the run; words are the
/// one unit both sides agree on, so aligning on them is what makes Pk and WindowDiff comparable
/// across conditions at all.</para>
/// </summary>
public static class GoldEvaluator
{
    public static DocumentMetrics Evaluate(GoldDocument gold, EvaluatedRun run)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(run);

        var referenceSegments = SegmentationMetrics.FromSegmentSizes(
            gold.Paragraphs.Select(TextMetrics.WordCount));
        var hypothesisSegments = SegmentationMetrics.FromSegmentSizes(
            run.Paragraphs.Select(p => p.WordCount));

        // The two segmentations can differ in total length when cleaning removed different
        // artifacts than the annotator did. Truncating to the shared prefix keeps the metric
        // defined; the artifact precision/recall below is where that difference is actually
        // scored, rather than being smeared across every boundary metric.
        var length = Math.Min(referenceSegments.Count, hypothesisSegments.Count);
        var reference = referenceSegments.Take(length).ToList();
        var hypothesis = hypothesisSegments.Take(length).ToList();

        var headingF1 = HeadingDetection(gold, run, out var levelAccuracy);
        var treeDistance = TreeEditDistance.Normalized(BuildGoldTree(gold), run.Root);

        return new DocumentMetrics(
            DocumentId: gold.DocumentId,
            Condition: gold.Condition,
            Family: gold.Family,
            Pk: SegmentationMetrics.Pk(reference, hypothesis),
            WindowDiff: SegmentationMetrics.WindowDiff(reference, hypothesis),
            BoundaryF1Exact: SegmentationMetrics.BoundaryF1(reference, hypothesis),
            BoundaryF1Tolerant: SegmentationMetrics.BoundaryF1(reference, hypothesis, tolerance: 1),
            HeadingF1: headingF1,
            HeadingLevelAccuracy: levelAccuracy,
            TreeEditDistance: treeDistance,
            ArtifactRemoval: ArtifactRemoval(gold, run),
            TextIntegrityPassed: run.Validation.Results
                .FirstOrDefault(r => r.CheckId == Checks.TextIntegrity)?.Passed ?? false,
            TreeSchemaPassed: run.Validation.Results
                .Where(r => r.CheckId is Checks.TreeShape or Checks.Contiguity or Checks.HeadingAnchor)
                .All(r => r.Passed),
            CostUsd: run.CostUsd,
            Duration: run.Duration);
    }

    /// <summary>
    /// pass^k (Appendix D): a document passes only if <em>every</em> one of its k runs clears all
    /// thresholds. It is the metric that catches a pipeline which is usually right — the one
    /// number that reflects what a user actually experiences.
    /// </summary>
    public static double PassAtK(IEnumerable<IReadOnlyList<DocumentMetrics>> runsPerDocument)
    {
        var documents = runsPerDocument.ToList();
        if (documents.Count == 0)
        {
            return 0;
        }

        var passed = documents.Count(runs =>
            runs.Count > 0 && runs.All(m => m.Passes(AcceptanceThresholds.For(m.Condition))));

        return (double)passed / documents.Count;
    }

    private static PrecisionRecall HeadingDetection(
        GoldDocument gold, EvaluatedRun run, out double levelAccuracy)
    {
        var goldByKey = new Dictionary<string, GoldHeading>(StringComparer.Ordinal);
        foreach (var heading in gold.Headings)
        {
            goldByKey.TryAdd(Key(heading.Text), heading);
        }

        var matchedLevels = 0;
        var matched = 0;
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var heading in run.Headings)
        {
            var key = Key(heading.Text);
            if (!goldByKey.TryGetValue(key, out var expected) || !claimed.Add(key))
            {
                continue;
            }

            matched++;
            if (heading.Level == expected.Level)
            {
                matchedLevels++;
            }
        }

        // Level accuracy is defined over CORRECTLY DETECTED headings only (plan §6): a pipeline
        // that finds nothing should score zero on detection, not a vacuous 100% on levels.
        levelAccuracy = matched == 0 ? double.NaN : (double)matchedLevels / matched;
        return PrecisionRecall.From(matched, run.Headings.Count, gold.Headings.Count);
    }

    private static PrecisionRecall ArtifactRemoval(GoldDocument gold, EvaluatedRun run)
    {
        var expected = gold.Artifacts.Select(Key).ToHashSet(StringComparer.Ordinal);
        var actual = run.ArtifactTexts.Select(Key).ToHashSet(StringComparer.Ordinal);
        return PrecisionRecall.From(actual.Count(a => expected.Contains(a)), actual.Count, expected.Count);
    }

    /// <summary>
    /// The reference tree, built from the gold headings and paragraph order by the same
    /// deterministic rule the pipeline uses for a fully-headed document — so tree edit distance
    /// measures the run's structural choices, not a difference in tree-building convention.
    ///
    /// <para>The interleaving is what makes this work. Line ids are the ordering key everywhere in
    /// the pipeline, so each gold unit is given one that puts it where the document puts it:
    /// paragraph <c>n</c> at line <c>10n</c>, and a heading that introduces paragraph <c>n</c> at
    /// <c>10n - 1</c>, just before it.</para>
    /// </summary>
    private static SectionNode BuildGoldTree(GoldDocument gold)
    {
        const int stride = 10;

        var paragraphs = gold.Paragraphs
            .Select((text, i) => new Paragraph(
                Ids.Paragraph(i), Ids.Line(i * stride), Ids.Line(i * stride), text,
                TextMetrics.WordCount(text), ParagraphKind.Body, Ids.Sha256Hex(text)))
            .ToList();

        var headings = gold.Headings
            .Select(h => new HeadingRecord(
                Ids.Line(Math.Max(0, h.BeforeParagraph * stride - 1)),
                h.Text, h.Level, 1.0, BoundarySource.Human, null))
            .ToList();

        return headings.Count == 0
            ? new SectionNode(
                Ids.Section(0), "Document", true, null, 1, [], [.. paragraphs.Select(p => p.ParagraphId)])
            : Tree.DeterministicTreeBuilder.Build(paragraphs, headings);
    }

    private static string Key(string text) => TextMetrics.NormalizeWhitespace(text).ToLowerInvariant();
}
