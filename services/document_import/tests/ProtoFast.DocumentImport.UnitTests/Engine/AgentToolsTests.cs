using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using Xunit;
using static ProtoFast.DocumentImport.UnitTests.Engine.EngineHarness;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class AgentToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EngineHarness _h = new();

    private async Task<(string RunId, ArtifactRef Input, AgentTools Tools)> OpenRunAsync()
    {
        var runId = DocumentImport.Core.DocumentImportIds.New();
        var input = await _h.InputAsync();
        await _h.Ledger.OpenAsync(runId, _h.Signature, RunMode.Discovery, Ct);
        return (runId, input, _h.Get<AgentToolsFactory>().ForRun(runId, _h.Signature, input, new TraceRef(runId), Ct));
    }

    [Fact]
    public async Task A_write_is_an_Orchestrator_record_whose_inputs_are_its_recorded_dependencies()
    {
        var (runId, input, tools) = await OpenRunAsync();

        var extracted = await tools.WriteArtifact("extract", Utf8("text"), Text, [input]);
        var summarised = await tools.WriteArtifact("summarise", Utf8("summary"), Markdown, [extracted.Ref]);

        var records = (await _h.Ledger.SummariseAsync(runId, Ct)).Stages;
        Assert.Equal(["extract", "summarise"], records.Select(r => r.StageId));
        Assert.All(records, r => Assert.Equal((Tier.Orchestrator, _h.Orchestrator), (r.Tier, r.Executor)));
        Assert.Empty(records[0].Stage.DependsOn);
        Assert.Equal(Raw, records[0].Stage.Input);
        Assert.Equal(["extract"], records[1].Stage.DependsOn);
        Assert.Equal(Text, records[1].Stage.Input);
        Assert.Equal(summarised.Ref, records[1].Output);
    }

    [Fact]
    public async Task A_stage_is_judged_by_the_family_verifiers_and_failures_go_back_to_the_agent()
    {
        var (runId, input, tools) = await OpenRunAsync();
        await tools.DefineVerifier(new VerifierSpec("clean", "extract", "No bad words."));

        var result = await tools.WriteArtifact("extract", Utf8("bad text"), Text, [input]);

        Assert.Equal(("clean", Verdict.Fail), (Assert.Single(result.Verdicts).VerifierId, result.Verdicts[0].Verdict));
        Assert.False(Assert.Single((await _h.Ledger.SummariseAsync(runId, Ct)).Stages).Passed);
        Assert.Equal(OutcomeKind.VerifierFail, Assert.Single(_h.Outcomes.Published).Kind);
    }

    [Fact]
    public async Task Redefining_a_verifier_differently_is_refused_and_redefining_it_identically_is_not()
    {
        var (_, _, tools) = await OpenRunAsync();
        var spec = new VerifierSpec("clean", "extract", "No bad words.");
        await tools.DefineVerifier(spec);

        Assert.Equal("clean", await tools.DefineVerifier(spec));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.DefineVerifier(spec with { Rubric = "Anything goes." }));
    }

    [Fact]
    public async Task A_delegation_is_recorded_at_the_delegate_tier_with_a_decision()
    {
        var (runId, input, tools) = await OpenRunAsync();
        var small = await _h.DelegateAsync("small", Tier.DelegateSmall, _ => "delegated");

        var record = await tools.Delegate("extract", small, [input], Text);

        var summary = await _h.Ledger.SummariseAsync(runId, Ct);
        Assert.Equal(record, Assert.Single(summary.Stages));
        Assert.Equal(Tier.DelegateSmall, record.Tier);
        Assert.Equal(Text, record.Stage.Output);
        Assert.Equal(new Decision("executor:extract", small.ToString(), "Delegated by the agent loop.", 1), Assert.Single(summary.Decisions));
    }

    [Fact]
    public async Task A_delegation_reuses_the_contract_the_stage_was_last_written_with()
    {
        var (_, input, tools) = await OpenRunAsync();
        var small = await _h.DelegateAsync("small", Tier.DelegateSmall, _ => "delegated");
        await tools.WriteArtifact("extract", Utf8("text"), Text, [input]);

        var record = await tools.Delegate("extract", small, [input]);

        Assert.Equal(Text, record.Stage.Output);
        await Assert.ThrowsAsync<ArgumentException>(() => tools.Delegate("never-seen", small, [input]));
    }

    [Fact]
    public async Task An_artifact_from_another_run_cannot_be_an_input()
    {
        var (_, _, tools) = await OpenRunAsync();
        var foreign = await _h.Artifacts.PutAsync("other-run", "extract", Utf8("x"), Text, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => tools.WriteArtifact("summarise", Utf8("y"), Markdown, [foreign]));
    }

    [Fact]
    public async Task An_agent_defined_executor_is_promoted_at_an_agent_tier_but_not_with_code()
    {
        var (_, _, tools) = await OpenRunAsync();
        var playbook = await tools.DefinePlaybook(
            new Playbook(new PlaybookRef("extract", 0), "Extract the text.", [], new Dictionary<string, string>()));
        var code = await tools.UploadCode(Utf8("assembly bytes"));

        var agentTier = await tools.DefineExecutor(new ExecutorSpec(
            new ExecutorRef("extractor", 0), Tier.DelegateMedium, ModelClasses.Medium, playbook, [], null, ExecutorOrigin.Seed, false));
        var codified = await tools.DefineExecutor(new ExecutorSpec(
            new ExecutorRef("extractor-code", 0), Tier.Codified, null, null, [], code, ExecutorOrigin.Seed, true));

        var agentSpec = await _h.Registry.ResolveAsync(agentTier, Ct);
        Assert.Equal((ExecutorOrigin.AgentDefined, true), (agentSpec.Origin, agentSpec.Promoted));
        Assert.False((await _h.Registry.ResolveAsync(codified, Ct)).Promoted);
        Assert.Equal([agentTier, codified], (await tools.Context()).Executors.Select(e => e.Ref));
    }

    [Fact]
    public async Task A_Codified_executor_must_name_uploaded_code()
    {
        var (_, _, tools) = await OpenRunAsync();
        var code = await tools.UploadCode(Utf8("assembly bytes"));

        await Assert.ThrowsAsync<ArgumentException>(() => tools.DefineExecutor(new ExecutorSpec(
            new ExecutorRef("extractor-code", 0), Tier.Codified, null, null, [], "not-uploaded", ExecutorOrigin.AgentDefined, false)));

        using var reader = new StreamReader(await _h.Registry.OpenCodeAsync(code, Ct));
        Assert.Equal("assembly bytes", await reader.ReadToEndAsync(Ct));
    }

    [Fact]
    public async Task A_playbook_is_versioned_and_its_examples_must_exist()
    {
        var (_, input, tools) = await OpenRunAsync();
        var output = await tools.WriteArtifact("extract", Utf8("text"), Text, [input]);
        var playbook = new Playbook(
            new PlaybookRef("extract", 0), "Extract the text.", [new Example(input, output.Ref)], new Dictionary<string, string>());

        Assert.Equal(new PlaybookRef("extract", 1), await tools.DefinePlaybook(playbook));
        Assert.Equal(new PlaybookRef("extract", 2), await tools.DefinePlaybook(playbook with { Instructions = "Extract it all." }));

        var missing = new ArtifactRef("run", "extract", "0000");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tools.DefinePlaybook(playbook with { Examples = [new Example(input, missing)] }));
    }

    [Theory]
    [InlineData(Tier.Orchestrator, null, true, null)]
    [InlineData(Tier.DelegateSmall, ModelClasses.Large, true, null)]
    [InlineData(Tier.DelegateSmall, ModelClasses.Small, false, null)]
    [InlineData(Tier.Codified, null, false, null)]
    [InlineData(Tier.Codified, ModelClasses.Small, false, "x.dll")]
    public async Task A_structurally_invalid_executor_is_refused(Tier tier, string? modelClass, bool playbook, string? assembly)
    {
        var (_, _, tools) = await OpenRunAsync();
        var spec = new ExecutorSpec(
            new ExecutorRef("x", 0), tier, modelClass, playbook ? new PlaybookRef("pb", 1) : null, [], assembly,
            ExecutorOrigin.AgentDefined, false);

        await Assert.ThrowsAsync<ArgumentException>(() => tools.DefineExecutor(spec));
    }

    [Fact]
    public async Task An_executor_another_family_defined_cannot_be_delegated_to()
    {
        var (_, input, tools) = await OpenRunAsync();
        var elsewhere = await _h.DelegateAsync("elsewhere", Tier.DelegateSmall, _ => "x", ExecutorOrigin.AgentDefined);

        await Assert.ThrowsAsync<ArgumentException>(() => tools.Delegate("extract", elsewhere, [input], Text));
    }

    [Fact]
    public async Task A_stage_scoped_loop_can_only_write_its_own_stage_and_its_writes_are_not_recorded()
    {
        var runId = DocumentImport.Core.DocumentImportIds.New();
        await _h.Ledger.OpenAsync(runId, _h.Signature, RunMode.Scheduled, Ct);
        var request = new StageRequest(runId, Stage("extract"), _h.Signature, [await _h.InputAsync()]);
        var tools = _h.Get<AgentToolsFactory>().ForStage(request, new TraceRef("t"), Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => tools.WriteArtifact("other", Utf8("x"), Markdown));
        await Assert.ThrowsAsync<ArgumentException>(() => tools.WriteArtifact("extract", Utf8("x"), Text));
        var written = await tools.WriteArtifact("extract", Utf8("x"), Markdown);

        Assert.Equal(written.Ref, tools.ScopedOutput);
        Assert.Empty((await _h.Ledger.SummariseAsync(runId, Ct)).Stages);
    }
}
