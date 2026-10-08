using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresRunBriefsTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"memo-{Guid.NewGuid():N}";

    private PostgresRunLedger Ledger => new(postgres.Contexts, TimeProvider.System);

    private PostgresRunBriefs Briefs => new(postgres.Contexts, TimeProvider.System);

    private static RunBrief Brief => new(
        "Delivered.", "Wrote the story.", [new BriefFlag("other", "Look at step 1.", 1)], [new StepBrief(1, "Read the input.")],
        "scripted", 0.03m);

    [Fact]
    public async Task Ended_runs_with_a_conversation_wait_for_a_brief_until_one_is_written()
    {
        var closed = await OpenAsync(conversation: true);
        await Ledger.CloseAsync(closed, null, Ct);
        var abandoned = await OpenAsync(conversation: true);
        await Ledger.AbandonAsync(abandoned, "turns spent", Ct);
        var open = await OpenAsync(conversation: true);
        var silent = await OpenAsync(conversation: false);
        await Ledger.CloseAsync(silent, null, Ct);
        string[] mine = [closed, abandoned, open, silent];

        var pending = await PendingAsync(mine);
        Assert.Equal([abandoned, closed], pending.Select(c => c.RunId));
        Assert.Equal((RunStatus.Abandoned, "turns spent", _family), (pending[0].Status, pending[0].Failure, pending[0].Family));

        Assert.True(await Briefs.ClaimAsync(closed, 3, Fresh, Ct));
        Assert.False(await Briefs.ClaimAsync(closed, 3, Fresh, Ct));
        Assert.Equal([abandoned], (await PendingAsync(mine)).Select(c => c.RunId));

        await Briefs.FailAsync(closed, "the model said nothing", Ct);
        Assert.Equal(
            (RunBriefStatus.Failed, 1, "the model said nothing"),
            await FindAsync(closed, b => (b.Status, b.Attempts, b.Error)));
        Assert.Contains(closed, (await PendingAsync(mine)).Select(c => c.RunId));

        Assert.True(await Briefs.ClaimAsync(closed, 3, Fresh, Ct));
        await Briefs.CompleteAsync(closed, Brief, Ct);
        var briefed = (await Briefs.FindAsync(closed, Ct))!;
        Assert.Equal((RunBriefStatus.Briefed, 2, (string?)null), (briefed.Status, briefed.Attempts, briefed.Error));
        Assert.Equivalent(Brief, briefed.Brief, strict: true);
        Assert.False(await Briefs.ClaimAsync(closed, 3, Fresh, Ct));
        Assert.DoesNotContain(closed, (await PendingAsync(mine)).Select(c => c.RunId));

        await Briefs.ResetAsync(closed, Ct);
        Assert.Null(await Briefs.FindAsync(closed, Ct));
        Assert.Contains(closed, (await PendingAsync(mine)).Select(c => c.RunId));
    }

    [Fact]
    public async Task A_stale_claim_is_taken_over_until_the_attempts_run_out()
    {
        var run = await OpenAsync(conversation: true);
        await Ledger.CloseAsync(run, null, Ct);

        Assert.True(await Briefs.ClaimAsync(run, 2, Fresh, Ct));
        Assert.True(await Briefs.ClaimAsync(run, 2, TimeSpan.Zero, Ct));
        await Briefs.FailAsync(run, "again", Ct);

        Assert.False(await Briefs.ClaimAsync(run, 2, TimeSpan.Zero, Ct));
        Assert.DoesNotContain(run, (await Briefs.PendingAsync(2, TimeSpan.Zero, 100, Ct)).Select(c => c.RunId));
        Assert.Equal((RunBriefStatus.Failed, 2), await FindAsync(run, b => (b.Status, b.Attempts)));
    }

    [Fact]
    public async Task Steps_are_recorded_once_and_reviewed_with_the_brief()
    {
        var run = await OpenAsync(conversation: true);
        var read = new RunStep(1, [new StepCall("c1", "execute_code", "read-artifact", "read-artifact", false,
            [new StepEffect(StepEffectKind.ArtifactRead, "Read the input characters 0–10 of 10", 0, "$input/h")])]);
        await Ledger.RecordStepAsync(run, new RunStep(3, [new StepCall("c2", "execute_skill", "context", null, true, [])]), Ct);
        await Ledger.RecordStepAsync(run, read, Ct);
        await Ledger.RecordStepAsync(run, read with { Calls = [] }, Ct);
        await Ledger.CloseAsync(run, null, Ct);
        Assert.True(await Briefs.ClaimAsync(run, 3, Fresh, Ct));
        await Briefs.CompleteAsync(run, Brief, Ct);

        var review = await new PostgresRunInspector(postgres.Contexts).ReviewAsync(run, Ct);

        Assert.Equal([1, 3], review.Steps.Select(s => s.Sequence));
        Assert.Equivalent(read, review.Steps[0], strict: true);
        Assert.Equal("Loaded context (error)", review.Steps[1].Headline);
        Assert.Equal("Delivered.", review.Briefing?.Brief?.Outcome);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Ledger.RecordStepAsync("no-such-run", read, Ct));
    }

    private async Task<IReadOnlyList<BriefCandidate>> PendingAsync(IReadOnlyCollection<string> mine) =>
        (await Briefs.PendingAsync(3, Fresh, 100, Ct)).Where(c => mine.Contains(c.RunId)).ToList();

    private async Task<T> FindAsync<T>(string runId, Func<RunBriefing, T> select) =>
        select((await Briefs.FindAsync(runId, Ct))!);

    private async Task<string> OpenAsync(bool conversation)
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, new DocumentSignature(_family, new Dictionary<string, string>()), RunMode.Discovery, Ct);
        if (conversation)
        {
            await Ledger.AppendTranscriptAsync(runId, 0, """{"message":{"role":"User","text":"go"}}""", Ct);
        }

        return runId;
    }
}
