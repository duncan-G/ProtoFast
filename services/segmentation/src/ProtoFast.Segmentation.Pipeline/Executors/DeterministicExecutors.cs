using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Triage;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Pipeline.Ingest;
using ProtoFast.Segmentation.Storage;
using Upload = ProtoFast.Segmentation.Data.Entities.Upload;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Phase 0 (plan §9.2, ingest plan §8): convert the upload if it needs it, read the Markdown,
/// parse it into lines, join the layout, compute statistics.
///
/// <para>Conversion sits in front of the read rather than beside it, and writes to the <em>same</em>
/// <c>.md</c> / <c>.layout.json</c> keys a hand-written Markdown upload would have occupied. That
/// is what keeps the rest of this method — and every phase after it — unaware that conversion
/// happened at all: a PDF and a Markdown file are the same thing by the time the extractor runs.</para>
///
/// <para>The upload is <em>copied</em> under the run prefix before anything else. That is what
/// makes a run self-contained: the <c>uploads/</c> prefix expires after seven days while run
/// artifacts live for thirty, and a run whose source vanished could not be re-run from phase 0.</para>
/// </summary>
public sealed class IngestExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    PhaseGate gate,
    IDocumentConverter converter,
    ILogger<IngestExecutor> logger)
    : Executor<RunStart, IngestComplete>(ExecutorIds.Ingest, declareCrossRunShareable: true)
{
    public override async ValueTask<IngestComplete> HandleAsync(
        RunStart message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Ingest);

        if (await gate.AlreadyDoneAsync(message.RunId, PipelinePhase.Ingest, key, message.FromPhase, cancellationToken)
            is { } done)
        {
            logger.LogDebug("Run {RunId}: ingest already complete; reusing {Key}", message.RunId, done.Key);
            return new IngestComplete(
                message.RunId,
                done,
                new ArtifactRef(message.RunId, ArtifactKeys.IngestStats(message.RunId), string.Empty, 0));
        }

        await journal.StartAsync(message.RunId, PipelinePhase.Ingest, key, cancellationToken);

        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Ingest, $"Run '{message.RunId}' is not in the database.");

        var upload = await journal.LoadUploadAsync(run.UploadId, cancellationToken)
            ?? throw new PipelineFailureException(
                PipelinePhase.Ingest,
                $"Upload '{run.UploadId}' is not in the database.",
                permanent: true);

        var markdownKey = ArtifactKeys.Upload(run.OwnerSubject, run.UploadId);
        var layoutKey = ArtifactKeys.UploadLayout(run.OwnerSubject, run.UploadId);

        var conversion = await ConvertIfNeededAsync(message.RunId, run.OwnerSubject, upload, cancellationToken);

        var markdown = await artifacts.Store.ReadTextAsync(markdownKey, cancellationToken)
            ?? throw new PipelineFailureException(
                PipelinePhase.Ingest,
                $"The upload '{markdownKey}' is missing. Uploads expire after 7 days; re-upload the document.");

        // Copy under the run prefix so later re-runs do not depend on the upload surviving.
        await artifacts.Store.WriteTextAsync(
            ArtifactKeys.RunSource(message.RunId), markdown, key, "text/markdown", cancellationToken);

        LayoutDocument? layout = null;
        if (await artifacts.Store.ExistsAsync(layoutKey, cancellationToken))
        {
            layout = await artifacts.Store.ReadAsync<LayoutDocument>(layoutKey, cancellationToken);

            // The layout is copied for the same reason the Markdown is: after seven days the
            // upload prefix is gone, and a re-run from phase 0 that lost its geometry would
            // silently produce a different tree from the one the user approved.
            await artifacts.Store.CopyAsync(
                layoutKey, ArtifactKeys.RunSourceLayout(message.RunId), cancellationToken);
        }

        if (conversion is not null)
        {
            await artifacts.Store.CopyAsync(
                ArtifactKeys.UploadConversion(run.OwnerSubject, run.UploadId),
                ArtifactKeys.RunConversion(message.RunId),
                cancellationToken);
        }

        var extraction = MarkdownExtractor.Extract(markdown, layout);

        logger.LogInformation(
            "Run {RunId}: extracted {Lines} lines from {Pages} pages (layout: {HasLayout}, family: {Family})",
            message.RunId, extraction.Lines.Count, extraction.Statistics.PageCount,
            extraction.Statistics.HasLayout, extraction.DocumentFamily);

        var lines = await artifacts.WriteLinesAsync(message.RunId, extraction.Lines, key, cancellationToken);
        var statistics = await artifacts.WriteStatisticsAsync(message.RunId, extraction.Statistics, key, cancellationToken);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Ingest, lines.Key,
            $"{extraction.Lines.Count} lines, family {extraction.DocumentFamily}", cancellationToken);

        return new IngestComplete(message.RunId, lines, statistics);
    }

    /// <summary>
    /// Hands a non-Markdown source to the converter, which writes the Markdown, the layout and the
    /// report to S3 itself. Returns null for a passthrough upload — the source <em>is</em> the
    /// Markdown, so the converter is never called (ingest plan C7) and an upload of a <c>.md</c>
    /// file costs exactly what it did before this feature existed.
    /// </summary>
    private async Task<ConversionResult?> ConvertIfNeededAsync(
        string runId, string ownerSubject, Upload upload, CancellationToken ct)
    {
        if (!upload.RequiresConversion)
        {
            return null;
        }

        var extension = string.IsNullOrEmpty(upload.SourceExtension) ? ".md" : upload.SourceExtension;

        await journal.NoteAsync(
            runId, PipelinePhase.Ingest, $"converting {extension.TrimStart('.')}", ct);

        var result = await converter.ConvertAsync(
            new ConversionRequest
            {
                UploadId = upload.UploadId,
                SourceKey = ArtifactKeys.UploadSource(ownerSubject, upload.UploadId, extension),
                MarkdownKey = ArtifactKeys.Upload(ownerSubject, upload.UploadId),
                LayoutKey = ArtifactKeys.UploadLayout(ownerSubject, upload.UploadId),
                ReportKey = ArtifactKeys.UploadConversion(ownerSubject, upload.UploadId),
                MediaType = upload.MediaType,
                FileName = upload.FileName,
                Ocr = converter.OcrRequest,
                // The converter joins the run's trace rather than starting one of its own, so a
                // slow phase 0 is explainable from the same trace as the rest of the run.
                Traceparent = System.Diagnostics.Activity.Current?.Id,
            },
            ct);

        var ocr = result.Ocr is { Applied: true } applied
            ? $", OCR on {applied.PagesOcred} page(s)"
            : string.Empty;

        await journal.NoteAsync(
            runId, PipelinePhase.Ingest, $"converted: {result.Pages} page(s){ocr}", ct);

        foreach (var warning in result.Warnings)
        {
            // A warning is not a failure: a thin result on a bad scan is often still what the user
            // wanted, and the event is what makes it explainable afterwards (ingest plan §12.3).
            await journal.NoteAsync(runId, PipelinePhase.Ingest, warning, ct);
        }

        logger.LogInformation(
            "Run {RunId}: converted {MediaType} in {Duration}ms ({Pages} pages, {Bytes} bytes of markdown, producer {Producer})",
            runId, upload.MediaType, result.DurationMs, result.Pages, result.MarkdownBytes, result.Producer);

        return result;
    }
}

/// <summary>Phase 1 (plan §9.3): the deterministic cleaning rules.</summary>
public sealed class CleanExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    IOptions<PipelineOptions> options,
    ILogger<CleanExecutor> logger)
    : Executor<IngestComplete, CleanComplete>(ExecutorIds.Clean, declareCrossRunShareable: true)
{
    public override async ValueTask<CleanComplete> HandleAsync(
        IngestComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Clean);
        await journal.StartAsync(message.RunId, PipelinePhase.Clean, key, cancellationToken);

        var lines = await artifacts.ReadLinesAsync(message.RunId, cancellationToken);
        var statistics = await artifacts.ReadStatisticsAsync(message.RunId, cancellationToken);

        var cleaning = new DocumentCleaner(options.Value).Clean(lines, statistics);

        logger.LogInformation(
            "Run {RunId}: cleaning removed {Artifacts} artifact lines, found {Boundaries} trusted boundaries, made {Edits} edits",
            message.RunId, cleaning.ArtifactLineIds.Count, cleaning.Boundaries.Count, cleaning.Edits.Count);

        var (clean, sidecar) = await artifacts.WriteCleaningAsync(message.RunId, cleaning, key, cancellationToken);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Clean, clean.Key,
            $"{cleaning.ArtifactLineIds.Count} artifacts removed, {cleaning.Boundaries.Count} trusted boundaries",
            cancellationToken);

        return new CleanComplete(message.RunId, clean, sidecar);
    }
}

/// <summary>
/// Phase 2 (plan §9.4): decide what a model needs to look at.
///
/// <para>Its output is the cheapest decision in the pipeline and the one with the largest effect
/// on cost — a document with no suspect regions skips phase 3 entirely and finishes in seconds
/// with no provider spend at all.</para>
/// </summary>
public sealed class TriageExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    IOptions<PipelineOptions> options,
    ILogger<TriageExecutor> logger)
    : Executor<CleanComplete, TriageComplete>(ExecutorIds.Triage, declareCrossRunShareable: true)
{
    public override async ValueTask<TriageComplete> HandleAsync(
        CleanComplete message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Triage);
        await journal.StartAsync(message.RunId, PipelinePhase.Triage, key, cancellationToken);

        var cleaning = await artifacts.ReadCleaningAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Triage, "The cleaning artifact is missing.");
        var statistics = await artifacts.ReadStatisticsAsync(message.RunId, cancellationToken);
        var run = await journal.LoadRunAsync(message.RunId, cancellationToken)!;

        var triage = new TriageAnalyzer(options.Value)
            .Analyze(cleaning, statistics, run?.DocumentFamily ?? FamilyDetector.Unknown);

        logger.LogInformation(
            "Run {RunId}: triage found {Suspect} suspect regions of {Total}; condition {Condition}",
            message.RunId, triage.SuspectRegions.Count, triage.Regions.Count, triage.Condition);

        var artifact = await artifacts.WriteTriageAsync(message.RunId, triage, key, cancellationToken);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Triage, artifact.Key,
            triage.CanSkipLabeling
                ? $"no suspect regions; condition {triage.Condition} — labelling skipped"
                : $"{triage.SuspectRegions.Count} suspect regions; condition {triage.Condition}",
            cancellationToken);

        return new TriageComplete(message.RunId, artifact, !triage.CanSkipLabeling);
    }
}

/// <summary>Phase 4 (plan §9.6): walk the labelled lines and build the paragraphs.</summary>
public sealed class AssembleExecutor(
    RunArtifacts artifacts,
    RunJournal journal,
    IOptions<PipelineOptions> options,
    ILogger<AssembleExecutor> logger)
    : Executor<LabelsMerged, AssembleComplete>(ExecutorIds.Assemble, declareCrossRunShareable: true)
{
    public override async ValueTask<AssembleComplete> HandleAsync(
        LabelsMerged message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var key = IdempotencyKeys.Phase(message.RunId, PipelinePhase.Assemble);
        await journal.StartAsync(message.RunId, PipelinePhase.Assemble, key, cancellationToken);

        var cleaning = await artifacts.ReadCleaningAsync(message.RunId, cancellationToken)
            ?? throw new PipelineFailureException(PipelinePhase.Assemble, "The cleaning artifact is missing.");
        var labels = await artifacts.ReadLabelsAsync(message.RunId, cancellationToken);

        var assembly = new ParagraphAssembler(options.Value).Assemble(cleaning, labels);

        logger.LogInformation(
            "Run {RunId}: assembled {Paragraphs} paragraphs and {Headings} headings ({Outliers} size outliers)",
            message.RunId, assembly.Paragraphs.Count, assembly.Headings.Count, assembly.SizeOutlierParagraphIds.Count);

        var (paragraphs, headings) = await artifacts.WriteAssemblyAsync(message.RunId, assembly, key, cancellationToken);

        await journal.CompleteAsync(
            message.RunId, PipelinePhase.Assemble, paragraphs.Key,
            $"{assembly.Paragraphs.Count} paragraphs, {assembly.Headings.Count} headings", cancellationToken);

        return new AssembleComplete(message.RunId, paragraphs, headings);
    }
}
