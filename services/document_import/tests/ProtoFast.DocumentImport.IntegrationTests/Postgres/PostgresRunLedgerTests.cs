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

    private Signature Signature => new(_family, new Dictionary<string, string> { ["pages"] = "3" });

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
        await Ledger.OpenAsync(runId, Signature, RunMode.Discovery, Ct);
        var record = Record(runId);
        await Ledger.RecordAsync(record, Ct);
        await Ledger.RecordAsync(runId, new Decision("executor:extract", "small@2", "delegated", 1), Ct);
        await Ledger.CloseAsync(runId, new TraceRef(runId), Ct);

        var summary = await Ledger.SummariseAsync(runId, Ct);

        Assert.Equal((runId, RunMode.Discovery, new TraceRef(runId)), (summary.RunId, summary.Mode, summary.Trace));
        Assert.Equal("3", summary.Signature.Facets["pages"]);
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
        await Ledger.OpenAsync(DocumentImportIds.New(), Signature, RunMode.Discovery, Ct);

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

        await Ledger.OpenAsync(runId, Signature, RunMode.Discovery, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ledger.OpenAsync(runId, Signature, RunMode.Discovery, Ct));
    }

    private async Task<string> ClosedRunAsync(RunMode mode)
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, Signature, mode, Ct);
        await Ledger.CloseAsync(runId, null, Ct);
        return runId;
    }
}
