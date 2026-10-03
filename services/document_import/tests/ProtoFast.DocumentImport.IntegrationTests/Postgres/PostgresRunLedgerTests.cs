using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresRunLedgerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"invoice-{Guid.NewGuid():N}";

    private PostgresRunLedger Ledger => new(postgres.Contexts, TimeProvider.System);

    private DocumentSignature DocumentSignature => new(_family, new Dictionary<string, string> { ["pages"] = "3" });

    private static StageRecord Record(string runId) =>
        new(
            runId,
            new StageDefinition("extract", [], new ContractRef("raw", 1), new ContractRef("text", 1), ["no-bad"], Budget.Unbounded),
            [new ArtifactRef(runId, ArtifactRef.InputStageId, "a1")],
            new ExecutorRef("small", 2),
            Tier.DelegateSmall,
            new StageResult(new ArtifactRef(runId, "extract", "b2"), new TraceRef("t"), new Cost(0.25m, TimeSpan.FromSeconds(3)),
                [new Decision("style", "terse", "short pages", 0.9)]),
            [new VerifierResult("no-bad", Verdict.Degraded, "meh", [new Finding("$.title", "empty")])],
            IsShadow: false);

    [Fact]
    public async Task A_run_round_trips_with_its_records_and_decisions()
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, DocumentSignature, RunMode.Discovery, Ct);
        var record = Record(runId);
        await Ledger.RecordAsync(record, Ct);
        await Ledger.RecordAsync(runId, new Decision("executor:extract", "small@2", "delegated", 1), Ct);
        await Ledger.CloseAsync(runId, new TraceRef(runId), Ct);

        var summary = await Ledger.SummariseAsync(runId, Ct);

        Assert.Equal((runId, RunMode.Discovery, new TraceRef(runId)), (summary.RunId, summary.Mode, summary.Trace));
        Assert.Equal("3", summary.DocumentSignature.Facets["pages"]);
        var stored = Assert.Single(summary.Stages);
        Assert.Equal(record.Stage.Verifiers, stored.Stage.Verifiers);
        Assert.Equal(record.Inputs, stored.Inputs);
        Assert.Equal(record.Result.Cost, stored.Result.Cost);
        Assert.Equal(record.Result.Decisions, stored.Result.Decisions);
        Assert.Equal(record.Verdicts[0].Findings, stored.Verdicts[0].Findings);
        Assert.True(stored.Degraded);
        Assert.Equal("small@2", Assert.Single(summary.Decisions).Choice);
    }

    [Fact]
    public async Task Recent_runs_are_closed_runs_of_the_mode_newest_first()
    {
        var first = await ClosedRunAsync(RunMode.Discovery);
        var second = await ClosedRunAsync(RunMode.Discovery);
        await ClosedRunAsync(RunMode.Scheduled);
        await Ledger.OpenAsync(DocumentImportIds.New(), DocumentSignature, RunMode.Discovery, Ct);

        var recent = await Ledger.RecentAsync(_family, RunMode.Discovery, 10, Ct);

        Assert.Equal([second, first], recent.Select(r => r.RunId));
        Assert.Equal(2, await Ledger.CountAsync(_family, RunMode.Discovery, Ct));
    }

    [Fact]
    public async Task A_run_must_be_opened_once_before_it_is_written()
    {
        var runId = DocumentImportIds.New();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Ledger.RecordAsync(Record(runId), Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Ledger.CloseAsync(runId, null, Ct));

        await Ledger.OpenAsync(runId, DocumentSignature, RunMode.Discovery, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.OpenAsync(runId, DocumentSignature, RunMode.Discovery, Ct));
    }

    [Fact]
    public async Task Progress_is_kept_per_source_and_each_report_replaces_the_last()
    {
        var (source, other) = (DocumentImportIds.New(), DocumentImportIds.New());
        var runId = DocumentImportIds.New();

        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Preparing), Ct);
        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Running, runId), Ct);
        await Ledger.ReportAsync(other, new RunProgress(RunPhase.Failed, Message: "unreadable"), Ct);

        var progress = await Ledger.ProgressAsync([source, other, DocumentImportIds.New()], Ct);

        Assert.Equal(2, progress.Count);
        Assert.Equal(new RunProgress(RunPhase.Running, runId), progress[source]);
        Assert.Equal(new RunProgress(RunPhase.Failed, Message: "unreadable"), progress[other]);
    }

    [Fact]
    public async Task A_started_stage_moves_only_the_source_following_that_run()
    {
        var (source, other) = (DocumentImportIds.New(), DocumentImportIds.New());
        var (runId, shadowRunId) = (DocumentImportIds.New(), DocumentImportIds.New());
        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Running, runId), Ct);
        await Ledger.ReportAsync(other, new RunProgress(RunPhase.Running, DocumentImportIds.New()), Ct);

        await Ledger.BeginStageAsync(runId, "scenes", Ct);
        await Ledger.BeginStageAsync(shadowRunId, "library", Ct);

        var progress = await Ledger.ProgressAsync([source, other], Ct);
        Assert.Equal("scenes", progress[source].StageId);
        Assert.Null(progress[other].StageId);
    }

    [Fact]
    public async Task Each_recorded_stage_adds_its_cost_to_the_source_across_retries()
    {
        var source = DocumentImportIds.New();
        var (first, second) = (DocumentImportIds.New(), DocumentImportIds.New());
        await Ledger.OpenAsync(first, DocumentSignature, RunMode.Discovery, Ct);
        await Ledger.OpenAsync(second, DocumentSignature, RunMode.Discovery, Ct);

        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Running, first), Ct);
        await Ledger.RecordAsync(Record(first), Ct);
        await Ledger.RecordAsync(Record(first), Ct);
        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Retrying, Message: "again"), Ct);
        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Running, second), Ct);
        await Ledger.RecordAsync(Record(second), Ct);

        Assert.Equal(0.75m, (await Ledger.ProgressAsync([source], Ct))[source].Cost);
    }

    [Fact]
    public async Task A_report_without_a_run_keeps_the_run_the_source_last_followed()
    {
        var source = DocumentImportIds.New();
        var runId = DocumentImportIds.New();
        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Running, runId), Ct);

        await Ledger.ReportAsync(source, new RunProgress(RunPhase.Retrying, Message: "again"), Ct);

        Assert.Equal(new RunProgress(RunPhase.Retrying, runId, Message: "again"), (await Ledger.ProgressAsync([source], Ct))[source]);
    }

    [Fact]
    public async Task An_open_run_is_found_until_it_is_closed_or_abandoned()
    {
        var (closed, abandoned) = (DocumentImportIds.New(), DocumentImportIds.New());
        await Ledger.OpenAsync(closed, DocumentSignature, RunMode.Discovery, Ct);
        await Ledger.OpenAsync(abandoned, DocumentSignature, RunMode.Discovery, Ct);
        await Ledger.RecordAsync(Record(abandoned), Ct);

        var open = await Ledger.FindOpenAsync(abandoned, Ct);
        Assert.NotNull(open);
        Assert.Equal((abandoned, RunMode.Discovery, 1), (open.RunId, open.Mode, open.Stages.Count));

        await Ledger.CloseAsync(closed, null, Ct);
        await Ledger.AbandonAsync(abandoned, "stopped short", Ct);

        Assert.Null(await Ledger.FindOpenAsync(closed, Ct));
        Assert.Null(await Ledger.FindOpenAsync(abandoned, Ct));
        Assert.Null(await Ledger.FindOpenAsync(DocumentImportIds.New(), Ct));
        Assert.Equal([closed], (await Ledger.RecentAsync(_family, RunMode.Discovery, 10, Ct)).Select(r => r.RunId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Ledger.AbandonAsync(DocumentImportIds.New(), "no run", Ct));
    }

    [Fact]
    public async Task A_transcript_is_appended_in_sequence_and_a_repeated_sequence_is_ignored()
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, DocumentSignature, RunMode.Discovery, Ct);

        await Ledger.AppendTranscriptAsync(runId, 0, """{"role":"user"}""", Ct);
        await Ledger.AppendTranscriptAsync(runId, 1, """{"role":"assistant"}""", Ct);
        await Ledger.AppendTranscriptAsync(runId, 1, """{"role":"other"}""", Ct);

        var transcript = await Ledger.TranscriptAsync(runId, Ct);
        Assert.Equal(2, transcript.Count);
        Assert.Contains("\"user\"", transcript[0]);
        Assert.Contains("\"assistant\"", transcript[1]);
        Assert.Empty(await Ledger.TranscriptAsync(DocumentImportIds.New(), Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Ledger.AppendTranscriptAsync(DocumentImportIds.New(), 0, "{}", Ct));
    }

    private async Task<string> ClosedRunAsync(RunMode mode)
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, DocumentSignature, mode, Ct);
        await Ledger.CloseAsync(runId, null, Ct);
        return runId;
    }
}
