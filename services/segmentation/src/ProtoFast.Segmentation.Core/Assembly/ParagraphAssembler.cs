using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Assembly;

/// <summary>Phase 4's output (plan §9.6). Size outliers are flagged, not fixed — that is phase 6's job.</summary>
public sealed record AssemblyResult(
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<HeadingRecord> Headings,
    IReadOnlyList<string> SizeOutlierParagraphIds);

/// <summary>A detected heading, carrying whatever level evidence exists so far.</summary>
public sealed record HeadingRecord(
    string LineId,
    string Text,
    int? Level,
    double Confidence,
    BoundarySource Source,
    LayoutFeatures? Layout);

/// <summary>
/// Phase 4: walks the labelled lines once and produces the paragraphs (plan §9.6).
///
/// <para>This is where the pipeline's central promise is kept — paragraph text is joined from
/// cleaned source lines by <see cref="LineJoiner"/>, the same code that built the integrity
/// baseline. No model output reaches <see cref="Paragraph.Text"/>, which is why
/// <c>text-integrity</c> can be a hard gate rather than a warning.</para>
/// </summary>
public sealed class ParagraphAssembler(PipelineOptions options)
{
    private readonly ParagraphOptions _paragraphs = options.Paragraphs;

    public AssemblyResult Assemble(
        CleaningResult cleaning,
        IReadOnlyList<LineLabelResult> labels)
    {
        ArgumentNullException.ThrowIfNull(cleaning);
        ArgumentNullException.ThrowIfNull(labels);

        var byId = cleaning.ContentLines.ToDictionary(l => l.LineId, StringComparer.Ordinal);
        var labelById = labels.ToDictionary(l => l.LineId, StringComparer.Ordinal);

        var paragraphs = new List<Paragraph>();
        var headings = new List<HeadingRecord>();
        var current = new List<LineRecord>();
        var currentKind = OtherKind.None;

        foreach (var line in cleaning.ContentLines)
        {
            if (!labelById.TryGetValue(line.LineId, out var label))
            {
                label = new LineLabelResult(line.LineId, LineLabel.Cont, null, 1.0);
            }

            if (label.Label == LineLabel.Artifact)
            {
                // Excluded from paragraphs but still part of the integrity accounting, which is
                // why the baseline is computed over ContentLines rather than over paragraphs.
                continue;
            }

            if (label.Label == LineLabel.Head)
            {
                Flush(paragraphs, current, currentKind, cleaning);
                current.Clear();
                currentKind = OtherKind.None;

                headings.Add(new HeadingRecord(
                    line.LineId, line.Text, label.HeadingLevel ?? line.HeadingLevelHint,
                    label.Confidence, SourceFor(cleaning, line.LineId), line.Layout));
                continue;
            }

            // A change of Other-kind is a block edge: a table row cannot continue a body
            // paragraph, and neither can the body line after the table.
            var startsNew = label.Label == LineLabel.Para
                || (current.Count > 0 && label.OtherKind != currentKind);

            if (startsNew && current.Count > 0)
            {
                Flush(paragraphs, current, currentKind, cleaning);
                current.Clear();
            }

            if (current.Count == 0)
            {
                currentKind = label.OtherKind;
            }

            current.Add(line);
        }

        Flush(paragraphs, current, currentKind, cleaning);

        var outliers = paragraphs
            .Where(p => p.Kind == ParagraphKind.Body
                && (p.WordCount < _paragraphs.MinWords || p.WordCount > _paragraphs.MaxWords))
            .Select(p => p.ParagraphId)
            .ToList();

        _ = byId;
        return new AssemblyResult(paragraphs, headings, outliers);
    }

    private void Flush(
        List<Paragraph> paragraphs,
        List<LineRecord> lines,
        OtherKind kind,
        CleaningResult cleaning)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var text = LineJoiner.Join(lines, cleaning.HyphenJoinedLineIds);
        if (text.Length == 0)
        {
            lines.Clear();
            return;
        }

        paragraphs.Add(new Paragraph(
            ParagraphId: Ids.Paragraph(paragraphs.Count),
            FirstLineId: lines[0].LineId,
            LastLineId: lines[^1].LineId,
            Text: text,
            WordCount: TextMetrics.WordCount(text),
            Kind: ToParagraphKind(kind),
            ContentHash: Ids.Sha256Hex(text))
        {
            LineIds = [.. lines.Select(l => l.LineId)],
        });
    }

    private static BoundarySource SourceFor(CleaningResult cleaning, string lineId) =>
        cleaning.Boundaries.FirstOrDefault(b => b.BeforeLineId == lineId)?.Source ?? BoundarySource.Llm;

    private static ParagraphKind ToParagraphKind(OtherKind kind) => kind switch
    {
        OtherKind.Caption => ParagraphKind.Caption,
        OtherKind.Footnote => ParagraphKind.Footnote,
        OtherKind.Table => ParagraphKind.Table,
        OtherKind.ListItem => ParagraphKind.ListBlock,
        OtherKind.Code => ParagraphKind.Code,
        OtherKind.Quote => ParagraphKind.Quote,
        OtherKind.Equation => ParagraphKind.Equation,
        _ => ParagraphKind.Body,
    };
}
