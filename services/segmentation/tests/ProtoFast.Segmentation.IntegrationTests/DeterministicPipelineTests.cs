using System.Text.Json;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The whole pipeline, end to end, with no provider configured.
///
/// <para>This is the plan's local-development promise made into a test (§22.1): a clean Markdown
/// fixture reaches <c>publish</c> on deterministic code alone. It is also the cheapest possible
/// regression test for the phase wiring — if any executor stops handing its message to the next,
/// this stops reaching phase 12.</para>
/// </summary>
public class DeterministicPipelineTests
{
    private const string CleanDocument = """
        # Calibration of Field Instruments

        ## 1 Introduction

        Field instruments drift over a season of transport and temperature cycling, and a device
        that read correctly at the factory will not read correctly in the field.

        This document describes the procedure used to detect that drift and to correct it.

        ## 2 Methods

        ### 2.1 Daily reference check

        Each morning the instrument is read against the reference standard kept in the field case,
        and the reading is recorded whether or not it differs from the expected value.

        ### 2.2 Monthly calibration

        The two-point calibration uses the zero standard and the span standard supplied with the
        instrument, both brought to ambient temperature before use.

        ## 3 Results

        Eleven of the fourteen instruments in the fleet required at least one unscheduled
        calibration over the season.
        """;

    [Fact]
    public async Task ACleanDocumentReachesPublishWithNoProvider()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(CleanDocument);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Completed, outcome);

        var run = await fixture.LoadRunAsync(runId);
        Assert.NotNull(run);
        Assert.Null(run!.Error);
        Assert.NotNull(run.PublishedAt);
        Assert.NotNull(run.TreeHash);

        // Labelling is SKIPPED, not done: that is the cheap path, and it is why this run cost
        // nothing (plan §9.4).
        var label = run.Phases.Single(p => p.Phase == PipelinePhase.Label);
        Assert.Equal(PhaseState.Skipped, label.State);

        Assert.All(
            run.Phases.Where(p => p.Phase is PipelinePhase.Ingest or PipelinePhase.Clean
                or PipelinePhase.Triage or PipelinePhase.Assemble or PipelinePhase.InferStructure
                or PipelinePhase.Validate or PipelinePhase.Freeze or PipelinePhase.Publish),
            p => Assert.Equal(PhaseState.Done, p.State));
    }

    [Fact]
    public async Task EveryPhaseLeavesAnArtifact()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(CleanDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        // "Every phase leaves a file" is one of the plan's design principles (§1) — it is what
        // makes a run inspectable and resumable rather than a black box.
        foreach (var phase in new[]
                 {
                     PipelinePhase.Ingest, PipelinePhase.Clean, PipelinePhase.Triage,
                     PipelinePhase.Label, PipelinePhase.Assemble, PipelinePhase.InferStructure,
                     PipelinePhase.Validate, PipelinePhase.Freeze, PipelinePhase.Publish,
                 })
        {
            Assert.Contains(ArtifactKeys.Phase(runId, phase), fixture.Artifacts.Keys);
        }

        // And the source, copied under the run prefix so the run survives the upload's expiry.
        Assert.Contains(ArtifactKeys.RunPrefix(runId) + "00_source.md", fixture.Artifacts.Keys);
    }

    [Fact]
    public async Task TheFrozenArtifactIsWrittenLockedAndCannotBeDeleted()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(CleanDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var frozenKey = ArtifactKeys.Phase(runId, PipelinePhase.Freeze);
        Assert.True(fixture.Artifacts.IsLocked(frozenKey));

        await fixture.Artifacts.DeleteAsync(frozenKey, TestContext.Current.CancellationToken);
        Assert.Contains(frozenKey, fixture.Artifacts.Keys);
    }

    [Fact]
    public async Task ReRunningIsFreeBecauseEveryPhaseChecksItsIdempotencyKey()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(CleanDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);
        var firstPass = fixture.Artifacts.WriteCounts[ArtifactKeys.Phase(runId, PipelinePhase.Ingest)];

        // A re-delivered SQS message, a resumed workflow, or a deploy that recreated the worker
        // mid-run all replay phases that already finished. Each must reuse its artifact (plan N4).
        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(firstPass, fixture.Artifacts.WriteCounts[ArtifactKeys.Phase(runId, PipelinePhase.Ingest)]);
    }

    [Fact]
    public async Task TheResultRowCarriesTheTreeAndTheParagraphs()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(CleanDocument, "calibration.md");

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var result = await fixture.LoadResultAsync(runId);
        Assert.NotNull(result);

        var root = JsonSerializer.Deserialize<SectionNode>(
            result!.TreeJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(root);

        // The document's own H1 becomes the root rather than getting a synthetic wrapper above it.
        Assert.Equal("Calibration of Field Instruments", root!.Title);
        Assert.False(root.TitleInferred);

        var titles = root.Descend().Select(n => n.Title).ToList();
        Assert.Contains("1 Introduction", titles);
        Assert.Contains("2.1 Daily reference check", titles);

        var paragraphs = JsonSerializer.Deserialize<List<Paragraph>>(
            result.ParagraphsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(paragraphs);
        Assert.NotEmpty(paragraphs!);
    }

    [Fact]
    public async Task ACancelledRunStopsWithoutPublishing()
    {
        await using var fixture = new PipelineHostFixture();
        var runId = await fixture.SubmitAsync(CleanDocument);

        await fixture.CancelAsync(runId);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Cancelled, outcome);
        Assert.Null((await fixture.LoadRunAsync(runId))!.PublishedAt);
    }

    [Fact]
    public async Task AMissingUploadFailsTheRunWithAnActionableMessage()
    {
        await using var fixture = new PipelineHostFixture();
        var runId = await fixture.SubmitAsync(CleanDocument);

        // Uploads expire after seven days; run artifacts live for thirty. A run whose source has
        // gone has to say so rather than failing with a null reference.
        await fixture.ForgetUploadAsync(runId);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Failed, outcome);
        var run = await fixture.LoadRunAsync(runId);
        Assert.Contains("re-upload", run!.Error!, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The freeze gate (plan §9.10). A document of a family nobody has approved yet stops for a
/// person; once enough of that family have been approved, the gate retires and later documents
/// go straight through. Both halves matter — the first is the safety property, the second is what
/// keeps human review from becoming the throughput ceiling.
/// </summary>
public class HumanGateTests
{
    private const string Document = """
        # Quarterly Report

        ## Summary

        Revenue rose by eleven percent over the quarter, driven mainly by the two new regions.

        ## Detail

        The northern region contributed the larger share, at roughly two thirds of the increase.
        """;

    [Fact]
    public async Task AnUnfamiliarFamilyStopsForAPerson()
    {
        await using var fixture = new PipelineHostFixture();
        var runId = await fixture.SubmitAsync(Document);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.AwaitingReview, outcome);

        // The review_tasks row is written as well as the checkpoint, so ThePlot can list the queue
        // without touching the workflow store — the review screen works even with no worker up.
        var review = await fixture.PendingReviewAsync(runId);
        Assert.NotNull(review);
        Assert.Equal("pending", review!.Status);

        var run = await fixture.LoadRunAsync(runId);
        Assert.Equal("pending", run!.ReviewState);
        Assert.Null(run.PublishedAt);
    }

    [Fact]
    public async Task AnApprovalResumesTheRunThroughToPublish()
    {
        await using var fixture = new PipelineHostFixture();
        var runId = await fixture.SubmitAsync(Document);

        Assert.Equal(
            RunOutcome.AwaitingReview,
            await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken));

        var review = await fixture.PendingReviewAsync(runId);
        await fixture.DecideAsync(review!.ReviewId, ReviewDecisionKind.Approve);

        var outcome = await fixture.Host.ResumeWithDecisionAsync(
            runId,
            new Pipeline.Executors.ReviewDecision(review.ReviewId, ReviewDecisionKind.Approve, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Completed, outcome);

        var run = await fixture.LoadRunAsync(runId);
        Assert.NotNull(run!.PublishedAt);
        Assert.Equal(PhaseState.Done, run.Phases.Single(p => p.Phase == PipelinePhase.HumanGate).State);
    }

    [Fact]
    public async Task OnceEnoughOfAFamilyAreApprovedTheGateRetires()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");

        var runId = await fixture.SubmitAsync(Document);
        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Completed, outcome);
        Assert.Null(await fixture.PendingReviewAsync(runId));
    }
}
