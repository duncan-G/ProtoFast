using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresPolicyStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"invoice-{Guid.NewGuid():N}";
    private readonly EngineOptions _options = new();

    private PostgresPolicyStore Store => new(postgres.Contexts, _options, TimeProvider.System);

    [Fact]
    public async Task A_stage_without_a_row_gets_the_default()
    {
        var snapshot = await Store.SnapshotAsync(_family, ["extract"], Ct);

        Assert.Equal(Tier.Orchestrator, snapshot["extract"].Primary);
        Assert.Equal(_options.Orchestrator, Assert.Single(snapshot["extract"].Ladder).Value);
    }

    [Fact]
    public async Task A_row_round_trips_and_is_replaced_on_put()
    {
        var at = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var row = PolicyRow.Default(_family, "extract", _options.Orchestrator, at) with
        {
            Ladder = new Dictionary<Tier, ExecutorRef>
            {
                [Tier.Orchestrator] = _options.Orchestrator,
                [Tier.DelegateSmall] = new("small", 3),
            },
            Primary = Tier.Orchestrator,
            Shadow = Tier.DelegateSmall,
            ShadowConfidence = new Confidence(5, 1),
        };

        await Store.PutAsync(row, Ct);
        await Store.PutAsync(row with { Confidence = new Confidence(7, 2) }, Ct);

        var stored = (await Store.SnapshotAsync(_family, ["extract", "summarise"], Ct))["extract"];
        Assert.Equal(new ExecutorRef("small", 3), stored.Ladder[Tier.DelegateSmall]);
        Assert.Equal((Tier.DelegateSmall, new Confidence(5, 1)), (stored.Shadow!.Value, stored.ShadowConfidence));
        Assert.Equal((new Confidence(7, 2), at), (stored.Confidence, stored.UpdatedAt));
    }
}
