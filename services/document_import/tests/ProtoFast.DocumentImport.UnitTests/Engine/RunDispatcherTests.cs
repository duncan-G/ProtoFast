using ProtoFast.DocumentImport.Engine;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class RunDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EngineHarness _h = new(o => o.Thresholds = o.Thresholds with { PromoteAt = 0.75 }, shadowRate: 1);
    private int _discoveryRuns;

    private RunDispatcher Dispatcher => _h.Get<RunDispatcher>();

    private async Task<ExecutorRef> ScriptDiscoveryAsync(bool withStrayStageOnFirstRun = false)
    {
        var small = await _h.DelegateAsync("small", Tier.DelegateSmall, _ => "extracted");
        _h.Agent.Run = async (input, tools) =>
        {
            var run = Interlocked.Increment(ref _discoveryRuns);

            // Without verifiers a stage can never leave Orchestrator, whatever the miner seeds.
            await tools.DefineVerifier(new VerifierSpec("extract-clean", "extract", "No bad words."));
            await tools.DefineVerifier(new VerifierSpec("summary-clean", "summarise", "No bad words."));

            var extracted = await tools.Delegate("extract", small, [input], Text);
            await tools.WriteArtifact("summarise", Utf8("summary"), Markdown, [extracted.Output]);
            if (withStrayStageOnFirstRun && run == 1)
            {
                await tools.WriteArtifact("translate", Utf8("traduction"), Markdown, [extracted.Output]);
            }
        };
        return small;
    }

    private async Task<WorkflowRef> MineAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.RunAsync(await _h.InputAsync(), Ct);
        }

        var workflow = new WorkflowRef($"mined:{Family}", 1);
        Assert.NotNull(await _h.Get<IMinedWorkflowStore>().GetAsync(workflow, Ct));
        return workflow;
    }

    [Fact]
    public async Task A_new_family_runs_in_discovery_and_records_what_the_agent_did()
    {
        await ScriptDiscoveryAsync();

        var summary = await Dispatcher.RunAsync(await _h.InputAsync(), Ct);

        Assert.Equal(RunMode.Discovery, summary.Mode);
        Assert.Equal(new TraceRef(summary.RunId), summary.Trace);
        Assert.Equal(["extract", "summarise"], summary.Stages.Select(s => s.StageId));
    }

    [Fact]
    public async Task The_miner_keeps_supported_stages_and_edges_and_seeds_the_delegate_as_primary()
    {
        var small = await ScriptDiscoveryAsync(withStrayStageOnFirstRun: true);

        var (_, mined) = (await _h.Get<IMinedWorkflowStore>().GetAsync(await MineAsync(), Ct))!.Value;

        var stages = mined.Workflow.Stages;
        Assert.Equal(["extract", "summarise"], stages.Select(s => s.Id));      // translate appeared in 1 of 3 runs
        Assert.Empty(stages[0].DependsOn);
        Assert.Equal(["extract"], stages[1].DependsOn);
        Assert.Equal((Raw, Text), (stages[0].Input, stages[0].Output));

        var extract = mined.Seeds.Single(s => s.StageId == "extract");
        Assert.Equal(Tier.DelegateSmall, extract.Primary);
        Assert.Equal(small, extract.Ladder[Tier.DelegateSmall]);
        Assert.Equal(new Confidence(4, 1), extract.Confidence);
        Assert.Equal(Tier.Orchestrator, mined.Seeds.Single(s => s.StageId == "summarise").Primary);

        Assert.False(await _h.Registry.IsPromotedAsync(mined.Workflow.Ref, Ct));
    }

    [Fact]
    public async Task A_promoted_workflow_shadows_discovery_until_it_flips_the_family_to_scheduled()
    {
        await ScriptDiscoveryAsync();
        var workflow = await MineAsync();

        // Mined but not promoted: still plain discovery, no scheduled runs.
        Assert.DoesNotContain(_h.Outcomes.Published, o => o.StageId is null);

        await _h.Get<WorkflowPromotion>().PromoteAsync(workflow, Ct);
        Assert.Equal(Tier.DelegateSmall, (await _h.Policies.GetAsync(Family, "extract", Ct)).Primary);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(RunMode.Discovery, (await Dispatcher.RunAsync(await _h.InputAsync(), Ct)).Mode);
        }

        Assert.Equal(3, _h.Outcomes.Published.Count(o => o.Kind == OutcomeKind.WorkflowShadowPass));
        Assert.Equal(RunMode.Scheduled, (await _h.Families.GetAsync(Family, Ct)).Mode);

        var before = _discoveryRuns;
        var scheduled = await Dispatcher.RunAsync(await _h.InputAsync(), Ct);

        Assert.Equal(RunMode.Scheduled, scheduled.Mode);
        Assert.Equal(before, _discoveryRuns);
        Assert.Equal(Tier.DelegateSmall, scheduled.Stages.Single(s => s.StageId == "extract").Tier);
        Assert.Equal(Tier.Orchestrator, scheduled.Stages.Single(s => s.StageId == "summarise").Tier);
    }

    [Fact]
    public async Task A_scheduled_family_whose_runs_fail_goes_back_to_discovery()
    {
        await ScriptDiscoveryAsync();
        var workflow = await MineAsync();
        await _h.Get<WorkflowPromotion>().PromoteAsync(workflow, Ct);
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.RunAsync(await _h.InputAsync(), Ct);
        }

        _h.Agent.RunStage = (request, tools) => tools.WriteArtifact(request.Stage.Id, Utf8("bad"), request.Stage.Output);
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<StageFailedException>(async () => await Dispatcher.RunAsync(await _h.InputAsync(), Ct));
        }

        var policy = await _h.Families.GetAsync(Family, Ct);
        Assert.Equal(RunMode.Discovery, policy.Mode);
        Assert.Null(policy.Workflow);
        Assert.Equal(RunMode.Discovery, (await Dispatcher.RunAsync(await _h.InputAsync(), Ct)).Mode);
    }
}
