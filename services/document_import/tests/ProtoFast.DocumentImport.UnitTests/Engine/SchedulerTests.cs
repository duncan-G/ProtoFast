using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class SchedulerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WorkflowDefinition Workflow(params StageDefinition[] stages) => new(new WorkflowRef("wf", 1), stages);

    private static async Task SetRowAsync(
        EngineHarness h, string stageId, Tier primary, Tier? shadow, params (Tier Tier, ExecutorRef Executor)[] ladder)
    {
        var row = PolicyRow.Default(Bucket, stageId, h.Orchestrator, h.Time.GetUtcNow());
        var rungs = new Dictionary<Tier, ExecutorRef>(row.Ladder);
        foreach (var (tier, executor) in ladder)
        {
            rungs[tier] = executor;
        }

        await h.Policies.PutAsync(row with { Ladder = rungs, Tier = primary, Shadow = shadow }, Ct);
    }

    [Fact]
    public async Task Primary_at_a_delegate_tier_produces_the_stage_output()
    {
        var h = new EngineHarness();
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "good");
        await SetRowAsync(h, "extract", Tier.DelegateSmall, null, (Tier.DelegateSmall, small));

        var summary = await h.Get<IScheduler>().RunAsync(Workflow(Stage("extract")), await h.InputAsync(), Ct);

        var record = Assert.Single(summary.Stages);
        Assert.Equal(RunMode.Scheduled, summary.Mode);
        Assert.Equal(small, record.Executor);
        Assert.Equal("good", await h.ReadAsync(record.Output));
        Assert.Equal(OutcomeKind.VerifierPass, Assert.Single(h.Outcomes.Published).Kind);
    }

    [Fact]
    public async Task A_failed_attempt_escalates_to_the_nearest_populated_tier_for_this_run_only()
    {
        var h = new EngineHarness();
        var large = await h.DelegateAsync("large", Tier.DelegateLarge, _ => "good");
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "bad");
        await SetRowAsync(h, "extract", Tier.DelegateSmall, null, (Tier.DelegateLarge, large), (Tier.DelegateSmall, small));

        var summary = await h.Get<IScheduler>().RunAsync(Workflow(Stage("extract")), await h.InputAsync(), Ct);

        Assert.Equal([small, large], summary.Stages.Select(s => s.Executor));   // Medium is unpopulated, so skipped
        Assert.False(summary.Stages[0].Passed);
        Assert.Equal("good", await h.ReadAsync(summary.Stages[^1].Output));

        // One failure is not enough evidence to demote; the row keeps its primary.
        Assert.Equal(Tier.DelegateSmall, (await h.Policies.GetAsync(Bucket, "extract", Ct)).Tier);
    }

    [Fact]
    public async Task An_Orchestrator_failure_fails_the_run_and_no_dependent_starts()
    {
        var h = new EngineHarness();
        var started = new ConcurrentBag<string>();
        h.Agent.RunStage = async (request, tools) =>
        {
            started.Add(request.Stage.Id);
            await tools.WriteArtifact(request.Stage.Id, Utf8("bad"), request.Stage.Output);
        };

        var e = await Assert.ThrowsAsync<StageFailedException>(() =>
            h.Get<IScheduler>().RunAsync(Workflow(Stage("a"), Stage("b", "a")), h.InputAsync().Result, Ct));

        Assert.Equal("a", e.Request.Stage.Id);
        Assert.Equal(["a"], started);
    }

    [Fact]
    public async Task Outputs_flow_to_dependents()
    {
        var h = new EngineHarness();
        var input = await h.InputAsync();

        var summary = await h.Get<IScheduler>().RunAsync(
            Workflow(Stage("d", "b", "c"), Stage("b", "a"), Stage("c", "a"), Stage("a")), input, Ct);

        var byStage = summary.Stages.ToDictionary(s => s.StageId);
        Assert.Equal([input], byStage["a"].Inputs);
        Assert.Equal([byStage["b"].Output, byStage["c"].Output], byStage["d"].Inputs);
        Assert.All(summary.Stages, s => Assert.NotNull(s.Result.Trace));
    }

    [Fact]
    public async Task A_shadow_is_recorded_and_published_but_never_used_as_output()
    {
        var h = new EngineHarness(shadowRate: 1);
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "shadow output");
        await SetRowAsync(h, "a", Tier.Orchestrator, Tier.DelegateSmall, (Tier.DelegateSmall, small));

        var summary = await h.Get<IScheduler>().RunAsync(Workflow(Stage("a"), Stage("b", "a")), await h.InputAsync(), Ct);

        var primary = summary.Stages.Single(s => s is { StageId: "a", IsShadow: false });
        var shadow = summary.Stages.Single(s => s is { StageId: "a", IsShadow: true });
        Assert.Equal(small, shadow.Executor);
        Assert.Equal([primary.Output], summary.Stages.Single(s => s.StageId == "b").Inputs);
        Assert.Contains(h.Outcomes.Published, o => o is { Kind: OutcomeKind.ShadowPass } && o.Executor == small);
        Assert.Equal(1, (await h.Policies.GetAsync(Bucket, "a", Ct)).ShadowConfidence.Observations);
    }

    [Fact]
    public async Task Running_out_of_time_is_a_failed_attempt_that_escalates()
    {
        var h = new EngineHarness();
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "hang");
        await SetRowAsync(h, "a", Tier.DelegateSmall, null, (Tier.DelegateSmall, small));
        var stage = Stage("a") with { Budget = new Budget(100, TimeSpan.FromMilliseconds(50)) };

        var summary = await h.Get<IScheduler>().RunAsync(Workflow(stage), await h.InputAsync(), Ct);

        Assert.Equal(EngineChecks.Budget, Assert.Single(summary.Stages[0].Verdicts).VerifierId);
        Assert.Equal(Tier.Orchestrator, summary.Stages[1].Tier);
        Assert.True(summary.Stages[1].Passed);
    }

    [Fact]
    public async Task A_faulting_executor_is_recorded_and_escalates()
    {
        var h = new EngineHarness();
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "throw");
        await SetRowAsync(h, "a", Tier.DelegateSmall, null, (Tier.DelegateSmall, small));

        var summary = await h.Get<IScheduler>().RunAsync(Workflow(Stage("a")), await h.InputAsync(), Ct);

        Assert.Equal(EngineChecks.ExecutorFaulted, Assert.Single(summary.Stages[0].Verdicts).VerifierId);
        Assert.True(summary.Stages[1].Passed);
    }

    [Fact]
    public async Task An_Orchestrator_result_without_a_trace_is_rejected()
    {
        var h = new EngineHarness();
        var untraced = await h.DelegateAsync("untraced", Tier.Orchestrator, _ => "good");
        await h.Policies.PutAsync(PolicyRow.Default(Bucket, "a", untraced, h.Time.GetUtcNow()), Ct);

        var e = await Assert.ThrowsAsync<StageFailedException>(() =>
            h.Get<IScheduler>().RunAsync(Workflow(Stage("a")), h.InputAsync().Result, Ct));

        Assert.Equal(EngineChecks.TraceRequired, Assert.Single(e.Verdicts).VerifierId);
    }

    [Fact]
    public async Task The_gate_never_routes_to_an_unpromoted_executor()
    {
        var h = new EngineHarness();
        var distilled = await h.DelegateAsync("distilled", Tier.DelegateLarge, _ => "good", ExecutorOrigin.Distilled);
        await SetRowAsync(h, "a", Tier.DelegateLarge, null, (Tier.DelegateLarge, distilled));

        var summary = await h.Get<IScheduler>().RunAsync(Workflow(Stage("a")), await h.InputAsync(), Ct);

        Assert.Equal(h.Orchestrator, Assert.Single(summary.Stages).Executor);

        await h.Registry.PromoteAsync(distilled, Ct);
        summary = await h.Get<IScheduler>().RunAsync(Workflow(Stage("a")), await h.InputAsync(), Ct);
        Assert.Equal(distilled, Assert.Single(summary.Stages).Executor);
    }

    [Fact]
    public async Task A_stage_with_no_verifiers_never_leaves_Orchestrator()
    {
        var h = new EngineHarness();
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "good");
        await SetRowAsync(h, "a", Tier.DelegateSmall, null, (Tier.DelegateSmall, small));

        var summary = await h.Get<IScheduler>().RunAsync(
            Workflow(Stage("a") with { Verifiers = [] }), await h.InputAsync(), Ct);

        Assert.Equal(Tier.Orchestrator, Assert.Single(summary.Stages).Tier);
    }

    [Fact]
    public async Task A_stage_with_no_deterministic_verifier_never_reaches_Codified()
    {
        var h = new EngineHarness();
        var small = await h.DelegateAsync("small", Tier.DelegateSmall, _ => "good");
        var code = await h.DelegateAsync("code", Tier.Codified, _ => "good");
        await SetRowAsync(h, "a", Tier.Codified, null, (Tier.DelegateSmall, small), (Tier.Codified, code));

        var judged = await h.Get<IScheduler>().RunAsync(
            Workflow(Stage("a") with { Verifiers = ["judge"] }), await h.InputAsync(), Ct);
        var checkedStage = await h.Get<IScheduler>().RunAsync(Workflow(Stage("a")), await h.InputAsync(), Ct);

        Assert.Equal(small, Assert.Single(judged.Stages).Executor);
        Assert.Equal(code, Assert.Single(checkedStage.Stages).Executor);
    }
}
