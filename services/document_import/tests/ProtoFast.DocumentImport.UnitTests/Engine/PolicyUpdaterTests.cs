using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class PolicyUpdaterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ExecutorRef Large = new("large", 1);
    private static readonly ExecutorRef Small = new("small", 1);

    private readonly EngineHarness _h = new();

    private IPolicyUpdater Updater => _h.Get<IPolicyUpdater>();

    private async Task<PolicyRow> SeedAsync(Tier primary, Tier? shadow, Confidence confidence = default)
    {
        var row = PolicyRow.Default(Family, "extract", _h.Orchestrator, _h.Time.GetUtcNow()) with
        {
            Ladder = new Dictionary<Tier, ExecutorRef>
            {
                [Tier.Orchestrator] = _h.Orchestrator,
                [Tier.DelegateLarge] = Large,
                [Tier.DelegateSmall] = Small,
            },
            Primary = primary,
            Shadow = shadow,
            Confidence = confidence == default ? Confidence.Prior : confidence,
        };
        await _h.Policies.PutAsync(row, Ct);
        return row;
    }

    private Task ApplyAsync(ExecutorRef executor, OutcomeKind kind, double weight = 1) =>
        Updater.ApplyAsync(new Outcome(Family, "extract", executor, kind, weight, _h.Time.GetUtcNow()), Ct);

    private Task<PolicyRow> RowAsync() => _h.Policies.GetAsync(Family, "extract", Ct);

    [Fact]
    public async Task A_primary_outcome_moves_the_primary_confidence()
    {
        await SeedAsync(Tier.DelegateLarge, null);

        await ApplyAsync(Large, OutcomeKind.VerifierPass);
        await ApplyAsync(Large, OutcomeKind.VerifierPass, Outcome.DegradedWeight);

        Assert.Equal(new Confidence(2.5, 1), (await RowAsync()).Confidence);
    }

    [Fact]
    public async Task An_escalation_fallback_earns_nothing()
    {
        var before = await SeedAsync(Tier.DelegateSmall, null);

        await ApplyAsync(Large, OutcomeKind.VerifierPass);

        Assert.Equal(before, await RowAsync());
    }

    [Fact]
    public async Task Promotion_makes_the_shadow_primary_and_carries_its_track_record()
    {
        await SeedAsync(Tier.DelegateLarge, Tier.DelegateSmall);

        for (var i = 0; i < 18; i++)
        {
            await ApplyAsync(Small, OutcomeKind.ShadowPass);
        }

        var row = await RowAsync();
        Assert.Equal(Tier.DelegateSmall, row.Primary);
        Assert.Equal(new Confidence(19, 1), row.Confidence);
        Assert.Null(row.Shadow);
        Assert.Equal(Confidence.Prior, row.ShadowConfidence);

        // Ready with no shadow, so the distiller is asked for the next rung.
        Assert.Equal(Tier.DelegateSmall, Assert.Single(_h.Distiller.Requests).Primary);
    }

    [Fact]
    public async Task Demotion_steps_left_and_sends_the_old_primary_back_to_shadow()
    {
        await SeedAsync(Tier.DelegateSmall, null, new Confidence(4, 3));

        await ApplyAsync(Small, OutcomeKind.VerifierFail);

        var row = await RowAsync();
        Assert.Equal(Tier.DelegateLarge, row.Primary);
        Assert.Equal(Confidence.Prior, row.Confidence);
        Assert.Equal(Tier.DelegateSmall, row.Shadow);
    }

    [Fact]
    public async Task A_freshly_demoted_primary_is_not_demoted_again_by_its_first_outcomes()
    {
        await SeedAsync(Tier.DelegateSmall, null, new Confidence(4, 3));
        await ApplyAsync(Small, OutcomeKind.VerifierFail);

        await ApplyAsync(Large, OutcomeKind.VerifierPass);
        await ApplyAsync(Large, OutcomeKind.VerifierFail);

        Assert.Equal(Tier.DelegateLarge, (await RowAsync()).Primary);
    }

    [Fact]
    public async Task An_external_correction_is_a_fail_at_triple_weight()
    {
        await SeedAsync(Tier.DelegateLarge, null);

        await Updater.ApplyAsync(Outcome.Correction(Family, "extract", Large, _h.Time.GetUtcNow()), Ct);

        Assert.Equal(new Confidence(1, 4), (await RowAsync()).Confidence);
    }

    [Fact]
    public async Task Confidence_decays_towards_the_prior_over_the_half_life()
    {
        await SeedAsync(Tier.DelegateLarge, null, new Confidence(11, 1));

        _h.Time.Advance(TimeSpan.FromDays(30));
        await ApplyAsync(Large, OutcomeKind.VerifierPass);

        var row = await RowAsync();
        Assert.Equal(new Confidence(7, 1), row.Confidence);
        Assert.Equal(_h.Time.GetUtcNow(), row.UpdatedAt);
    }

    [Fact]
    public async Task A_workflow_in_shadow_flips_its_family_to_scheduled_when_ready_and_back_when_it_regresses()
    {
        var workflow = new WorkflowRef("mined:invoice", 1);
        await _h.Families.PutAsync(DocumentFamilyPolicy.Default(Family, _h.Time.GetUtcNow()) with { Workflow = workflow }, Ct);

        for (var i = 0; i < 18; i++)
        {
            await Updater.ApplyAsync(Outcome.ForWorkflow(Family, workflow, true, false, _h.Time.GetUtcNow()), Ct);
        }

        Assert.Equal(RunMode.Scheduled, (await _h.Families.GetAsync(Family, Ct)).Mode);

        // An older workflow's outcome does not count.
        await Updater.ApplyAsync(
            Outcome.ForWorkflow(Family, workflow with { Version = 0 }, false, false, _h.Time.GetUtcNow()), Ct);
        Assert.Equal(new Confidence(19, 1), (await _h.Families.GetAsync(Family, Ct)).Confidence);

        for (var i = 0; i < 4; i++)
        {
            await Updater.ApplyAsync(Outcome.ForWorkflow(Family, workflow, false, false, _h.Time.GetUtcNow()), Ct);
        }

        var policy = await _h.Families.GetAsync(Family, Ct);
        Assert.Equal(RunMode.Discovery, policy.Mode);
        Assert.Null(policy.Workflow);
    }
}
