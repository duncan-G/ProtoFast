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

public class PostgresRunInspectorTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"memo-{Guid.NewGuid():N}";

    private PostgresRunLedger Ledger => new(postgres.Contexts, TimeProvider.System);

    private PostgresRunInspector Inspector => new(postgres.Contexts);

    [Fact]
    public async Task Lists_a_familys_runs_across_generations_newest_first_with_what_each_cost()
    {
        var first = await OpenAsync(_family, RunMode.Discovery);
        await Ledger.RecordAsync(Attempt(first, "extract", Verdict.Pass, 0.25m), Ct);
        await Ledger.CloseAsync(first, new TraceRef("t1"), Ct);
        var second = await OpenAsync($"{_family}#1", RunMode.Scheduled);
        await Ledger.RecordAsync(Attempt(second, "extract", Verdict.Fail, 0.5m), Ct);
        await Ledger.RecordAsync(Attempt(second, "extract", Verdict.Pass, 0.75m), Ct);
        await Ledger.RecordAsync(Attempt(second, "extract", Verdict.Fail, 1m, shadow: true), Ct);
        await Ledger.ReportAsync("upload-1", new RunProgress(RunPhase.Running, second), Ct);

        var page = await Inspector.ListAsync(new RunListQuery(_family), Ct);

        Assert.Equal(2, page.Total);
        Assert.Equal([second, first], page.Runs.Select(r => r.RunId));
        var open = page.Runs[0];
        Assert.Equal((RunStatus.Open, 3, true, 2.25m, "upload-1"), (open.Status, open.StageAttempts, open.Passed, open.Cost, open.SourceId));
        var closed = page.Runs[1];
        Assert.Equal((RunStatus.Closed, 1, true, 0.25m, (string?)null), (closed.Status, closed.StageAttempts, closed.Passed, closed.Cost, closed.SourceId));
        Assert.Equal(new TraceRef("t1"), closed.Trace);
    }

    [Fact]
    public async Task Filters_by_mode_and_status_and_pages()
    {
        var discovery = await OpenAsync(_family, RunMode.Discovery);
        var scheduled = await OpenAsync(_family, RunMode.Scheduled);
        var abandoned = await OpenAsync(_family, RunMode.Discovery);
        await Ledger.AbandonAsync(abandoned, "turns spent", Ct);

        var scheduledOnly = await Inspector.ListAsync(new RunListQuery(_family, Mode: RunMode.Scheduled), Ct);
        var abandonedOnly = await Inspector.ListAsync(new RunListQuery(_family, Status: RunStatus.Abandoned), Ct);
        var openOnly = await Inspector.ListAsync(new RunListQuery(_family, Status: RunStatus.Open), Ct);
        var secondPage = await Inspector.ListAsync(new RunListQuery(_family, Page: 1, PageSize: 2), Ct);

        Assert.Equal([scheduled], scheduledOnly.Runs.Select(r => r.RunId));
        Assert.Equal("turns spent", Assert.Single(abandonedOnly.Runs).Failure);
        Assert.Equal(2, openOnly.Total);
        Assert.DoesNotContain(openOnly.Runs, r => r.RunId == abandoned);
        Assert.Equal(3, secondPage.Total);
        Assert.Equal([discovery], secondPage.Runs.Select(r => r.RunId));
    }

    [Fact]
    public async Task A_run_fails_when_its_last_real_attempt_at_a_stage_failed()
    {
        var runId = await OpenAsync(_family, RunMode.Discovery);
        await Ledger.RecordAsync(Attempt(runId, "extract", Verdict.Pass, 0.1m), Ct);
        await Ledger.RecordAsync(Attempt(runId, "story", Verdict.Pass, 0.1m), Ct);
        await Ledger.RecordAsync(Attempt(runId, "story", Verdict.Fail, 0.1m), Ct);

        var detail = await Inspector.FindAsync(runId, Ct);

        Assert.False(detail!.Header.Passed);
    }

    [Fact]
    public async Task A_run_detail_carries_its_attempts_decisions_progress_and_message_count_in_order()
    {
        var runId = await OpenAsync(_family, RunMode.Discovery);
        await Ledger.RecordAsync(Attempt(runId, "extract", Verdict.Degraded, 0.1m), Ct);
        await Ledger.RecordAsync(Attempt(runId, "story", Verdict.Pass, 0.2m), Ct);
        await Ledger.RecordAsync(runId, new Decision("executor:story", "small@2", "cheap", 0.8), Ct);
        await Ledger.AppendTranscriptAsync(runId, 0, """{"message":{"role":"User","text":"go"}}""", Ct);
        await Ledger.AppendTranscriptAsync(runId, 1, """{"message":{"role":"Assistant","text":"done"},"spent":{"amount":0.2}}""", Ct);
        await Ledger.ReportAsync("upload-2", new RunProgress(RunPhase.Finished, runId, ResultId: "story-1"), Ct);

        var detail = await Inspector.FindAsync(runId, Ct);

        Assert.Equal(["extract", "story"], detail!.Stages.Select(s => s.Record.StageId));
        Assert.True(detail.Stages[0].Record.Degraded);
        Assert.Equal("small@2", Assert.Single(detail.Decisions).Decision.Choice);
        Assert.Equal((RunPhase.Finished, "story-1"), (detail.Progress!.Phase, detail.Progress.ResultId));
        Assert.Equal(2, detail.MessageCount);
        Assert.Equal("upload-2", detail.Header.SourceId);
    }

    [Fact]
    public async Task The_transcript_pages_forward_from_a_sequence()
    {
        var runId = await OpenAsync(_family, RunMode.Discovery);
        for (var i = 0; i < 5; i++)
        {
            await Ledger.AppendTranscriptAsync(runId, i, $$$"""{"message":{"role":"User","text":"{{{i}}}"}}""", Ct);
        }

        await Ledger.RecordSystemPromptAsync(runId, 0, "You are the discovery agent.", Ct);

        var first = await Inspector.TranscriptAsync(runId, fromSequence: 0, take: 2, Ct);
        var page = await Inspector.TranscriptAsync(runId, fromSequence: 2, take: 2, Ct);

        Assert.Equal("You are the discovery agent.", Assert.Single(first.SystemPrompts).Prompt);
        Assert.Empty(page.SystemPrompts);
        Assert.Equal(5, page.Total);
        Assert.Equal([2, 3], page.Messages.Select(m => m.Sequence));
        // jsonb re-spaces what it stores, so only the value is checked.
        Assert.Contains("\"2\"", page.Messages[0].Json);
    }

    [Fact]
    public async Task An_unknown_run_is_null()
    {
        Assert.Null(await Inspector.FindAsync(DocumentImportIds.New(), Ct));
    }

    private async Task<string> OpenAsync(string family, RunMode mode)
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, new DocumentSignature(family, new Dictionary<string, string> { ["pages"] = "1" }), mode, Ct);
        return runId;
    }

    private static StageRecord Attempt(string runId, string stageId, Verdict verdict, decimal cost, bool shadow = false) =>
        new(
            runId,
            new StageDefinition(stageId, [], new ContractRef("raw", 1), new ContractRef("text", 1), ["check"], Budget.Unbounded),
            [new ArtifactRef(runId, ArtifactRef.InputStageId, "a1")],
            new ExecutorRef("small", 2),
            Tier.DelegateSmall,
            new StageResult(new ArtifactRef(runId, stageId, "b2"), null, new Cost(cost, TimeSpan.FromSeconds(1)), []),
            [new VerifierResult("check", verdict, "because", [])],
            shadow);
}
