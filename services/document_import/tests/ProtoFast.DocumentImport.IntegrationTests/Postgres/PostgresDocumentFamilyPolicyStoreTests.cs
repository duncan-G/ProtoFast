using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresDocumentFamilyPolicyStoreTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"invoice-{Guid.NewGuid():N}";

    private PostgresDocumentFamilyPolicyStore Store => new(postgres.Contexts, TimeProvider.System);

    [Fact]
    public async Task A_new_family_starts_in_discovery()
    {
        var policy = await Store.GetAsync(_family, Ct);

        Assert.Equal((RunMode.Discovery, null), (policy.Mode, policy.Workflow));
    }

    [Fact]
    public async Task A_policy_round_trips_and_its_workflow_can_be_cleared()
    {
        var policy = DocumentFamilyPolicy.Default(_family, DateTimeOffset.UtcNow) with
        {
            Mode = RunMode.Scheduled,
            Workflow = new WorkflowRef("mined:invoice", 2),
            Confidence = new Confidence(19, 1),
        };

        await Store.PutAsync(policy, Ct);
        var stored = await Store.GetAsync(_family, Ct);
        Assert.Equal((RunMode.Scheduled, new WorkflowRef("mined:invoice", 2), new Confidence(19, 1)),
            (stored.Mode, stored.Workflow, stored.Confidence));

        await Store.PutAsync(stored with { Mode = RunMode.Discovery, Workflow = null }, Ct);
        Assert.Null((await Store.GetAsync(_family, Ct)).Workflow);
    }
}
