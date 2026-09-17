using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Triage;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Runs the deterministic phases (0, 1, 2, 4) over a Markdown string. Most tests here are about
/// what the pipeline does end to end for a document that never reaches a model, so they all need
/// the same four calls wired the same way.
/// </summary>
internal static class Fixtures
{
    public static PipelineOptions Options() => new();

    public static DeterministicRun Run(string markdown, LayoutDocument? layout = null, PipelineOptions? options = null)
    {
        options ??= Options();

        var extraction = MarkdownExtractor.Extract(markdown, layout);
        var cleaning = new DocumentCleaner(options).Clean(extraction.Lines, extraction.Statistics);
        var triage = new TriageAnalyzer(options).Analyze(cleaning, extraction.Statistics, extraction.DocumentFamily);

        // With no suspect regions there is nothing to label, so the merge is pure precedence:
        // trusted boundaries, cleaning continuations, then "continues the paragraph".
        var labels = LabelMerger.Merge(cleaning.ContentLines, cleaning, new Dictionary<int, IReadOnlyList<LineLabelResult>>(), []);
        var assembly = new ParagraphAssembler(options).Assemble(cleaning, labels);
        var headings = Core.Tree.HeadingLevelAssigner.Assign(assembly.Headings);

        return new DeterministicRun(extraction, cleaning, triage, labels, assembly with { Headings = headings });
    }

    /// <summary>Layout whose every line is plain body text — the "nothing stands out" baseline.</summary>
    public static LayoutDocument BodyLayout(IEnumerable<string> texts, double fontSize = 10, int linesPerPage = 40)
    {
        var lines = new List<RawLayoutLine>();
        var index = 0;
        foreach (var text in texts)
        {
            lines.Add(new RawLayoutLine
            {
                Page = index / linesPerPage + 1,
                Text = text,
                X = 50,
                Y = 50 + index % linesPerPage * 14,
                Width = 500,
                Height = 12,
                PageWidth = 600,
                PageHeight = 800,
                FontSize = fontSize,
            });
            index++;
        }

        return new LayoutDocument { Lines = lines };
    }
}

internal sealed record DeterministicRun(
    ExtractionResult Extraction,
    CleaningResult Cleaning,
    TriageResult Triage,
    IReadOnlyList<LineLabelResult> Labels,
    AssemblyResult Assembly);
