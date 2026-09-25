using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresMinedWorkflowStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PostgresMinedWorkflowStore Store => new(postgres.Contexts, TimeProvider.System);

    [Fact]
    public async Task A_draft_round_trips_with_its_seeds()
    {
        var workflow = new WorkflowRef($"mined:{Guid.NewGuid():N}", 1);
        var stage = new StageDefinition("extract", [], new ContractRef("raw", 1), new ContractRef("text", 1), ["clean"], Budget.Unbounded);
        var seed = PolicyRow.Default("invoice", "extract", new EngineOptions().Orchestrator, DateTimeOffset.UtcNow);

        await Store.PutAsync("invoice", new MinedWorkflow(new WorkflowDefinition(workflow, [stage]), [seed]), Ct);

        var (family, mined) = (await Store.GetAsync(workflow, Ct))!.Value;
        Assert.Equal(("invoice", workflow, "extract"), (family, mined.Workflow.Ref, mined.Workflow.Stages[0].Id));
        Assert.Equal(Tier.Orchestrator, Assert.Single(mined.Seeds).Primary);
        Assert.Null(await Store.GetAsync(workflow with { Version = 2 }, Ct));
    }
}
