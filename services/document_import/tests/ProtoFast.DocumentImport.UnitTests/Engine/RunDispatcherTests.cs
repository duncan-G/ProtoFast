using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Scheduling;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
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
    public async Task A_run_reports_itself_and_each_stage_it_starts_under_the_input_source()
    {
        await ScriptDiscoveryAsync();
        var input = await _h.InputAsync();

        var summary = await Dispatcher.RunAsync(input, Ct);

        var progress = (await _h.Ledger.ProgressAsync([input.RunId], Ct))[input.RunId];
        Assert.Equal(new RunProgress(RunPhase.Running, summary.RunId, "summarise", Cost: 1), progress);
    }

    [Fact]
    public async Task A_run_interrupted_outside_the_agent_is_resumed_with_its_records_on_the_next_attempt()
    {
        var small = await _h.DelegateAsync("small", Tier.DelegateSmall, _ => "extracted");
        var attempts = 0;
        _h.Agent.Run = async (input, tools) =>
        {
            if (++attempts == 1)
            {
                await tools.DefineVerifier(new VerifierSpec("extract-clean", "extract", "No bad words."));
                await tools.Delegate("extract", small, [input], Text);
                throw new IOException("the database went away");
            }

            var extracted = Assert.Single(await tools.Records());
            Assert.Equal(("extract", true), (extracted.StageId, extracted.Passed));
            await tools.WriteArtifact("summarise", Utf8("summary"), Markdown, [extracted.Output]);
        };
        var input = await _h.InputAsync();

        await Assert.ThrowsAsync<IOException>(() => Dispatcher.RunAsync(input, Ct));
        var interrupted = (await _h.Ledger.ProgressAsync([input.RunId], Ct))[input.RunId];
        Assert.Equal(RunPhase.Running, interrupted.Phase);

        var summary = await Dispatcher.RunAsync(input, Ct);

        Assert.Equal(interrupted.RunId, summary.RunId);
        Assert.Equal(["extract", "summarise"], summary.Stages.Select(s => s.StageId));
        Assert.Null(await _h.Ledger.FindOpenAsync(summary.RunId, Ct));
        Assert.Single(await _h.Ledger.RecentAsync(Family, RunMode.Discovery, 10, Ct));
    }

    [Fact]
    public async Task A_resumed_run_keeps_the_family_it_opened_under_when_the_classifier_changes_its_mind()
    {
        var attempts = 0;
        _h.Agent.Run = async (input, tools) =>
        {
            if (++attempts == 1)
            {
                throw new IOException("the database went away");
            }

            await tools.WriteArtifact("summarise", Utf8("summary"), Markdown, [input]);
        };
        var input = await _h.InputAsync();
        await Assert.ThrowsAsync<IOException>(() => Dispatcher.RunAsync(input, Ct));
        var interrupted = (await _h.Ledger.ProgressAsync([input.RunId], Ct))[input.RunId];

        _h.Classifier.Family = "memo";
        var summary = await Dispatcher.RunAsync(input, Ct);

        Assert.Equal((interrupted.RunId, Family), (summary.RunId, summary.DocumentSignature.Family));
        Assert.Empty(await _h.Ledger.RecentAsync("memo", RunMode.Discovery, 10, Ct));
        Assert.Single(await _h.Ledger.RecentAsync(Family, RunMode.Discovery, 10, Ct));
    }

    [Fact]
    public async Task A_run_that_finished_on_the_same_input_is_reused_rather_than_run_again()
    {
        await ScriptDiscoveryAsync();
        var input = await _h.InputAsync();
        var finished = await Dispatcher.RunAsync(input, Ct);

        var again = await Dispatcher.RunAsync(input, Ct);

        Assert.Equal(finished.RunId, again.RunId);
        Assert.Equal(1, _discoveryRuns);
        Assert.Single(await _h.Ledger.RecentAsync(Family, RunMode.Discovery, 10, Ct));
    }

    [Fact]
    public async Task A_source_whose_input_changed_since_its_finished_run_runs_again()
    {
        await ScriptDiscoveryAsync();
        var input = await _h.InputAsync();
        var finished = await Dispatcher.RunAsync(input, Ct);
        var changed = await _h.Artifacts.PutAsync(input.RunId, ArtifactRef.InputStageId, Utf8("input, normalised"), Raw, Ct);

        var again = await Dispatcher.RunAsync(changed, Ct);

        Assert.NotEqual(finished.RunId, again.RunId);
        Assert.Equal(2, _discoveryRuns);
    }

    [Fact]
    public async Task A_run_the_agent_gives_up_on_is_abandoned_and_never_resumed()
    {
        var runs = 0;
        _h.Agent.Run = (_, _) => ++runs == 1
            ? throw new DiscoveryFailedException("stopped short")
            : Task.CompletedTask;
        var input = await _h.InputAsync();

        var failed = await Assert.ThrowsAsync<DiscoveryFailedException>(() => Dispatcher.RunAsync(input, Ct));
        Assert.Equal("stopped short", failed.Message);
        var abandoned = (await _h.Ledger.ProgressAsync([input.RunId], Ct))[input.RunId].RunId!;
        Assert.Null(await _h.Ledger.FindOpenAsync(abandoned, Ct));

        var summary = await Dispatcher.RunAsync(input, Ct);

        Assert.NotEqual(abandoned, summary.RunId);
        Assert.DoesNotContain(await _h.Ledger.RecentAsync(Family, RunMode.Discovery, 10, Ct), r => r.RunId == abandoned);
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

    [Fact]
    public async Task A_reset_family_rediscovers_without_the_skills_runs_or_workflow_of_its_earlier_generation()
    {
        await ScriptDiscoveryAsync();
        var workflow = await MineAsync();
        await _h.Get<WorkflowPromotion>().PromoteAsync(workflow, Ct);
        _h.Agent.Run = async (_, tools) => await tools.DefineSkill(
            new Skill(new SkillRef("read-prose", 0), "When the input is prose.", "Split on chapters.", []));
        await Dispatcher.RunAsync(await _h.InputAsync(), Ct);
        var shadowed = _h.Outcomes.Published.Count(o => o.Kind is OutcomeKind.WorkflowShadowPass or OutcomeKind.WorkflowShadowFail);

        Assert.Equal(1, await _h.Get<IDocumentFamilyGenerations>().ResetAsync(Family, Ct));

        DocumentFamilyContext? context = null;
        IReadOnlyList<Skill>? skills = null;
        _h.Agent.Run = async (_, tools) => (context, skills) = (await tools.Context(), await tools.Skills());
        var summary = await Dispatcher.RunAsync(await _h.InputAsync(), Ct);

        Assert.Equal((RunMode.Discovery, $"{Family}#1"), (summary.Mode, summary.DocumentSignature.Family));
        Assert.Equal((0, 0, 0), (context!.StageIds.Count, context.Executors.Count, context.Verifiers.Count));
        Assert.Empty(skills!);
        Assert.Equal(shadowed, _h.Outcomes.Published.Count(o => o.Kind is OutcomeKind.WorkflowShadowPass or OutcomeKind.WorkflowShadowFail));
        Assert.Equal(4, (await _h.Ledger.RecentAsync(Family, RunMode.Discovery, 10, Ct)).Count);
    }
}
