using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Triage;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Evaluation;

/// <summary>Everything the deterministic phases produced for one document.</summary>
public sealed record DeterministicRunResult(
    ExtractionResult Extraction,
    CleaningResult Cleaning,
    TriageResult Triage,
    IReadOnlyList<LineLabelResult> Labels,
    AssemblyResult Assembly,
    SectionNode Root,
    ValidationReport Validation);

/// <summary>
/// Phases 0, 1, 2, 3b, 4 and 5 with no I/O at all.
///
/// <para>This is the whole pipeline minus the model calls, and for a clean document it is the
/// whole pipeline full stop — triage skips labelling and the tree is built from trusted headings
/// (plan §9.4, §22.1).</para>
///
/// <para>It lives in Core, beside the metrics, because three callers need exactly this: the CLI's
/// <c>segment</c> and <c>evaluate</c> commands, and the CI evaluation gate. A second copy of the
/// phase order in any of them would be a second thing to keep in step with the pipeline.</para>
/// </summary>
public static class DeterministicPipeline
{
    public static DeterministicRunResult Run(string markdown, LayoutDocument? layout, PipelineOptions? options = null)
    {
        options ??= new PipelineOptions();

        var extraction = MarkdownExtractor.Extract(markdown, layout);
        var cleaning = new DocumentCleaner(options).Clean(extraction.Lines, extraction.Statistics);
        var triage = new TriageAnalyzer(options).Analyze(cleaning, extraction.Statistics, extraction.DocumentFamily);

        // With no model available, the merge is pure precedence: trusted boundaries, cleaning
        // continuations, then "continues the paragraph". A suspect region simply gets the
        // deterministic answer, which the report below makes visible rather than hiding.
        var labels = LabelMerger.Merge(
            cleaning.ContentLines, cleaning, new Dictionary<int, IReadOnlyList<LineLabelResult>>(), []);

        var assembled = new ParagraphAssembler(options).Assemble(cleaning, labels);
        var headings = HeadingLevelAssigner.Assign(assembled.Headings);
        var assembly = assembled with { Headings = headings };

        var levelled = ApplyLevels(labels, headings);

        var root = headings.Count > 0
            ? DeterministicTreeBuilder.Build(assembly.Paragraphs, headings)
            : new SectionNode(
                Ids.Section(0), "Document", true, null, 1, [],
                [.. assembly.Paragraphs.Select(p => p.ParagraphId)]);

        var validation = Checks.CheckAll(
            cleaning, levelled, assembly.Paragraphs, assembly.Headings, root,
            options.Paragraphs, assembly.SizeOutlierParagraphIds.ToHashSet(StringComparer.Ordinal));

        return new DeterministicRunResult(extraction, cleaning, triage, levelled, assembly, root, validation);
    }

    /// <summary>Writes the assigned heading levels back onto the labels, as phase 3b does.</summary>
    private static IReadOnlyList<LineLabelResult> ApplyLevels(
        IReadOnlyList<LineLabelResult> labels, IReadOnlyList<HeadingRecord> headings)
    {
        var levels = headings.ToDictionary(h => h.LineId, h => h.Level ?? 1, StringComparer.Ordinal);

        return
        [
            .. labels.Select(l => l.Label == LineLabel.Head && levels.TryGetValue(l.LineId, out var level)
                ? l with { HeadingLevel = level }
                : l),
        ];
    }

}
