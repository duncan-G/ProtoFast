using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// Phase 0's conversion step (ingest plan §8, §24).
///
/// <para>The property every test here defends is that conversion is invisible downstream: it
/// writes to the same keys a Markdown upload would have occupied, so phases 1–12 cannot tell the
/// difference. The two things that are <em>not</em> invisible are the ones asserted directly — a
/// Markdown upload must never reach the converter, and a re-run must not convert twice.</para>
/// </summary>
public class ConversionIngestTests
{
    private const string ConvertedDocument = """
        # Annual Report

        ## 1 Overview

        The fleet operated for two hundred and eleven days this season, which is eleven days more
        than the plan allowed for and the most since the programme began.

        Three instruments were retired during the season and were not replaced.

        ## 2 Finances

        Operating costs rose by four per cent, almost entirely in transport, and the reserve was
        not drawn on at any point during the year.
        """;

    [Fact]
    public async Task AMarkdownUploadNeverReachesTheConverter()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(ConvertedDocument);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Completed, outcome);

        // The source IS the markdown for a passthrough upload, so calling the converter would be
        // paying for a round trip that could only produce what we already have (ingest plan C7).
        Assert.Empty(fixture.Converter.Calls);
    }

    [Fact]
    public async Task APdfIsConvertedAndThenIngestedLikeAnyMarkdown()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitConvertibleAsync(ConvertedDocument);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Completed, outcome);

        var call = Assert.Single(fixture.Converter.Calls);
        Assert.Equal("application/pdf", call.MediaType);
        Assert.EndsWith(".pdf", call.SourceKey, StringComparison.Ordinal);
        Assert.EndsWith(".md", call.MarkdownKey, StringComparison.Ordinal);

        // The run is self-contained afterwards: the Markdown and the report are both under the run
        // prefix, which outlives the seven-day upload expiry.
        Assert.Contains(ArtifactKeys.RunSource(runId), fixture.Artifacts.Keys);
        Assert.Contains(ArtifactKeys.RunConversion(runId), fixture.Artifacts.Keys);

        var run = await fixture.LoadRunAsync(runId);
        Assert.NotNull(run!.PublishedAt);
    }

    [Fact]
    public async Task TheConvertersLayoutIsJoinedAndCopiedUnderTheRun()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");

        fixture.Converter.Layout = new LayoutDocument
        {
            Producer = "stub/pdfplumber",
            Lines =
            [
                new RawLayoutLine
                {
                    Page = 1,
                    Text = "Annual Report",
                    X = 72,
                    Y = 90,
                    Width = 260,
                    Height = 22,
                    PageWidth = 612,
                    PageHeight = 792,
                    FontSize = 22,
                    Bold = true,
                },
            ],
        };

        var runId = await fixture.SubmitConvertibleAsync(ConvertedDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        // Scoped to phase 0 deliberately. A layout that covers one line of a document is exactly
        // the "partial" condition triage exists to catch, so this run routes to a model and stops
        // at a registry with none in it — which is correct behaviour and not what is under test
        // here. What is under test is that the converter's geometry reached the pipeline at all.
        var run = await fixture.LoadRunAsync(runId);
        Assert.Equal(PhaseState.Done, run!.Phases.Single(p => p.Phase == PipelinePhase.Ingest).State);

        // Geometry a converter produced is treated exactly as a hand-supplied layout used to be —
        // which is the whole reason the converter writes to the existing key.
        Assert.Contains(ArtifactKeys.RunSourceLayout(runId), fixture.Artifacts.Keys);

        var statistics = await fixture.Artifacts.ReadAsync<DocumentStatistics>(
            ArtifactKeys.IngestStats(runId), TestContext.Current.CancellationToken);
        Assert.True(statistics!.HasLayout);
    }

    [Fact]
    public async Task ReRunningFromPhaseZeroDoesNotConvertASecondTime()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitConvertibleAsync(ConvertedDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);
        Assert.Single(fixture.Converter.Calls);

        // A redelivered message replays phase 0. The phase gate is what makes that free, and
        // conversion is the most expensive thing behind it (ingest plan C10, N5).
        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Single(fixture.Converter.Calls);
    }

    [Fact]
    public async Task AnUnconvertibleDocumentFailsTheRunPermanently()
    {
        await using var fixture = new PipelineHostFixture();
        var runId = await fixture.SubmitConvertibleAsync(ConvertedDocument);

        fixture.Converter.Fail = new PipelineFailureException(
            PipelinePhase.Ingest, "ThePlot could not read this PDF: it has no extractable content.", permanent: true);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        // Permanent, so the consumer deletes the message: five delivery attempts would reach the
        // same answer and put a bad upload in a DLQ that is meant to signal a system fault.
        Assert.Equal(RunOutcome.FailedPermanently, outcome);

        var run = await fixture.LoadRunAsync(runId);
        Assert.Contains("could not read this PDF", run!.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableConverterLeavesTheRunRetryable()
    {
        await using var fixture = new PipelineHostFixture();
        var runId = await fixture.SubmitConvertibleAsync(ConvertedDocument);

        fixture.Converter.Fail = new PipelineFailureException(
            PipelinePhase.Ingest, "The document converter is not responding.");

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        // Transient: the message goes back on the queue, so runs submitted during a converter
        // deploy queue up rather than dying in it.
        Assert.Equal(RunOutcome.Failed, outcome);
    }
}
