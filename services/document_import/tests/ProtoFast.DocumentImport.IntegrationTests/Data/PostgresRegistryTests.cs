using System.Text;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Data;

public class PostgresRegistryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FrozenObjectStore _objects = new();

    private PostgresRegistry Registry => new(postgres.Contexts, _objects, TimeProvider.System);

    private static string UniqueId(string name) => $"{name}-{Guid.NewGuid():N}";

    private static ExecutorSpec Delegate(string id, ExecutorOrigin origin = ExecutorOrigin.AgentDefined) =>
        new(new ExecutorRef(id, 0), Tier.DelegateSmall, ModelClasses.Small, new PlaybookRef("pb", 1), ["read"], null, origin, false);

    [Fact]
    public async Task Publishing_assigns_the_next_version_and_resolves_the_content()
    {
        var id = UniqueId("extractor");
        var spec = Delegate(id);

        var first = await Registry.PublishAsync(spec, Ct);
        var second = await Registry.PublishAsync(spec with { Tools = ["read", "write"] }, Ct);

        Assert.Equal((new ExecutorRef(id, 1), new ExecutorRef(id, 2)), (first, second));
        var resolved = await Registry.ResolveAsync(second, Ct);
        Assert.Equal(["read", "write"], resolved.Tools);
        Assert.Equal((second, true), (resolved.Ref, resolved.Promoted));
    }

    [Fact]
    public async Task Unchanged_content_is_stored_once()
    {
        var spec = Delegate(UniqueId("extractor"));

        await Registry.PublishAsync(spec, Ct);
        await Registry.PublishAsync(spec, Ct);

        Assert.Equal(1, _objects.Writes);
    }

    [Fact]
    public async Task Concurrent_publishes_get_distinct_versions()
    {
        var id = UniqueId("extractor");

        var refs = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Registry.PublishAsync(Delegate(id) with { Tools = [$"tool-{i}"] }, Ct)));

        Assert.Equal(Enumerable.Range(1, 10), refs.Select(r => r.Version).Order());
    }

    [Fact]
    public async Task Code_and_distilled_executors_wait_for_a_human()
    {
        var code = await Registry.PublishCodeAsync(new MemoryStream(Encoding.UTF8.GetBytes("assembly")), Ct);
        var codified = await Registry.PublishAsync(
            new ExecutorSpec(new ExecutorRef(UniqueId("code"), 0), Tier.Codified, null, null, [], code, ExecutorOrigin.AgentDefined, false),
            Ct);
        var distilled = await Registry.PublishAsync(Delegate(UniqueId("distilled"), ExecutorOrigin.Distilled), Ct);

        Assert.False((await Registry.ResolveAsync(codified, Ct)).Promoted);
        Assert.False((await Registry.ResolveAsync(distilled, Ct)).Promoted);

        await Registry.PromoteAsync(codified, Ct);
        Assert.True((await Registry.ResolveAsync(codified, Ct)).Promoted);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Registry.PromoteAsync(new ExecutorRef("missing", 1), Ct));
    }

    [Fact]
    public async Task A_workflow_round_trips_and_is_promoted_by_a_human()
    {
        var stage = new StageDefinition(
            "extract", [], new ContractRef("raw", 1), new ContractRef("text", 1), ["no-bad"], Budget.Unbounded);
        var workflow = await Registry.PublishAsync(
            new WorkflowDefinition(new WorkflowRef(UniqueId("mined"), 0), [stage, stage with { Id = "summarise", DependsOn = ["extract"] }]),
            Ct);

        Assert.False(await Registry.IsPromotedAsync(workflow, Ct));
        await Registry.PromoteAsync(workflow, Ct);
        Assert.True(await Registry.IsPromotedAsync(workflow, Ct));

        var resolved = await Registry.ResolveAsync(workflow, Ct);
        Assert.Equal(workflow, resolved.Ref);
        Assert.Equal(Budget.Unbounded, resolved.Stages[0].Budget);
        Assert.Equal(["extract"], resolved.Stages[1].DependsOn);
    }

    [Fact]
    public async Task A_playbook_round_trips()
    {
        var example = new Example(new ArtifactRef("run", "$input", "a1"), new ArtifactRef("run", "extract", "b2"));
        var playbook = await Registry.PublishAsync(
            new Playbook(new PlaybookRef(UniqueId("extract"), 0), "Extract the text.", [example],
                new Dictionary<string, string> { ["style"] = "terse" }),
            Ct);

        var resolved = await Registry.ResolveAsync(playbook, Ct);

        Assert.Equal(("Extract the text.", example, "terse"), (resolved.Instructions, resolved.Examples[0], resolved.Rules["style"]));
    }

    [Fact]
    public async Task Code_is_addressed_by_its_hash_and_a_hash_cannot_carry_a_path()
    {
        var code = await Registry.PublishCodeAsync(new MemoryStream(Encoding.UTF8.GetBytes("assembly")), Ct);

        Assert.True(await Registry.CodeExistsAsync(code, Ct));
        using var reader = new StreamReader(await Registry.OpenCodeAsync(code, Ct));
        Assert.Equal("assembly", await reader.ReadToEndAsync(Ct));

        Assert.False(await Registry.CodeExistsAsync("../playbooks/x", Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Registry.OpenCodeAsync("../playbooks/x", Ct));
    }
}
