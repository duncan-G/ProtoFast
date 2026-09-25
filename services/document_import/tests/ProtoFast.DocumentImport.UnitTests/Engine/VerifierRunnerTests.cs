using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class VerifierRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EngineHarness _h = new();

    private async Task<IReadOnlyList<VerifierResult>> RunAsync(
        string content, IReadOnlyList<string> verifiers, decimal cost = 0, decimal maxCost = decimal.MaxValue)
    {
        var output = await _h.Artifacts.PutAsync("run", "a", Utf8(content), Markdown, Ct);
        var stage = Stage("a") with { Verifiers = verifiers, Budget = new Budget(maxCost, Timeout.InfiniteTimeSpan) };
        var request = new StageRequest("run", stage, _h.Signature, []);
        var result = new StageResult(output, null, new Cost(cost, TimeSpan.Zero), []);
        return await _h.Get<VerifierRunner>().RunAsync(request, result, Tier.DelegateSmall, Ct);
    }

    [Fact]
    public async Task Deterministic_verifiers_run_first_and_a_fail_stops_the_rest()
    {
        var verdicts = await RunAsync("bad", ["judge", "no-bad"]);

        var only = Assert.Single(verdicts);
        Assert.Equal("no-bad", only.VerifierId);
        Assert.Equal(Verdict.Fail, only.Verdict);
    }

    [Fact]
    public async Task A_degraded_verdict_does_not_stop_the_rest()
    {
        var verdicts = await RunAsync("meh", ["judge", "no-bad"]);

        Assert.Equal(["no-bad", "judge"], verdicts.Select(v => v.VerifierId));
        Assert.All(verdicts, v => Assert.Equal(Verdict.Degraded, v.Verdict));
    }

    [Fact]
    public async Task Spending_over_the_cost_budget_fails_before_any_verifier_runs()
    {
        var verdicts = await RunAsync("good", ["no-bad"], cost: 2, maxCost: 1);

        Assert.Equal(EngineChecks.Budget, Assert.Single(verdicts).VerifierId);
    }

    [Fact]
    public async Task An_unknown_verifier_id_is_a_configuration_error() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("good", ["missing"]));
}
