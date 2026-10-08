using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class StageGraphTests
{
    private static WorkflowDefinition Workflow(params StageDefinition[] stages) => new(new WorkflowRef("wf", 1), stages);

    [Fact]
    public void Validate_rejects_a_cycle()
    {
        var e = Assert.Throws<InvalidWorkflowException>(() => StageGraph.Validate(Workflow(Stage("a", "b"), Stage("b", "a"))));
        Assert.Contains("cycle", e.Message);
    }

    [Fact]
    public void Validate_rejects_an_unknown_dependency() =>
        Assert.Throws<InvalidWorkflowException>(() => StageGraph.Validate(Workflow(Stage("a", "missing"))));

    [Fact]
    public void Validate_rejects_a_duplicate_stage() =>
        Assert.Throws<InvalidWorkflowException>(() => StageGraph.Validate(Workflow(Stage("a"), Stage("a"))));

    [Fact]
    public void Validate_rejects_the_reserved_input_stage_id() =>
        Assert.Throws<InvalidWorkflowException>(() => StageGraph.Validate(Workflow(Stage(ArtifactRef.InputStageId))));

    [Fact]
    public async Task RunDag_starts_each_stage_after_its_dependencies()
    {
        var finished = new ConcurrentQueue<string>();
        IReadOnlyList<StageDefinition> stages = [Stage("d", "b", "c"), Stage("b", "a"), Stage("c", "a"), Stage("a")];

        await stages.RunDagAsync(() => { }, async s =>
        {
            await Task.Delay(s.Id == "b" ? 30 : 1);
            finished.Enqueue(s.Id);
        });

        var order = finished.ToList();
        Assert.Equal("a", order[0]);
        Assert.Equal("d", order[^1]);
        Assert.Equal(["a", "c", "b", "d"], order);  // c does not wait for the slower b
    }

    [Fact]
    public async Task RunDag_rethrows_the_first_failure_and_skips_its_dependents()
    {
        var ran = new ConcurrentBag<string>();
        var errors = 0;
        IReadOnlyList<StageDefinition> stages = [Stage("a"), Stage("b", "a")];

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => stages.RunDagAsync(
            () => Interlocked.Increment(ref errors),
            s =>
            {
                ran.Add(s.Id);
                return s.Id == "a" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            }));

        Assert.Equal("boom", e.Message);
        Assert.Equal(["a"], ran);
        Assert.Equal(1, errors);
    }
}
