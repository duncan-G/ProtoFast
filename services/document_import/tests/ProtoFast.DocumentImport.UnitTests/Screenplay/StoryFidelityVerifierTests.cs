using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.Screenplay.Verifiers;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public class StoryFidelityVerifierTests
{
    private const string Manuscript = """
        THE QUIET YEAR
        by A. Writer

        Page 1

        The kitchen was small and yellow, and the radio on the sill had not worked in years. Mara
        shuffled in, bleary, and flicked it on anyway. "Get out," she said to the cat, slamming the
        cupboard door. Nothing happened, which was how most mornings went in that house.

        Page 2

        Later she found the letter under the bread bin, three pages in her mother's hand, and read it
        standing up because sitting down would have made it real.
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ServiceProvider _services;

    public StoryFidelityVerifierTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentWorkflowEngine();
        services.AddInMemoryWorkflowEngineStores();
        services.AddScreenplayDiscovery();
        services.AddSingleton<ILanguageModelFactory>(new ScriptedLanguageModelFactory());
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public void A_split_with_a_repaired_edge_keeps_the_manuscript_and_a_paraphrase_does_not()
    {
        var faithful = TextOverlap.Measure(Manuscript,
        [
            new TextUnit("$.title", "The Quiet Year"),
            new TextUnit("a", "The kitchen was small and yellow, and the radio on the sill had not worked in years."),
            new TextUnit("b", "Mara shuffled in, bleary, and flicked it on anyway."),
            new TextUnit("c", "Get out"),
            new TextUnit("d", "She slams the cupboard door."),
            new TextUnit("e", "Nothing happened, which was how most mornings went in that house."),
            new TextUnit("f", "Later she found the letter under the bread bin, three pages in her mother's hand, and read it standing up because sitting down would have made it real."),
        ]);

        Assert.True(faithful.Kept >= StoryFidelityVerifier.MinKept, $"kept {faithful.Kept}");
        Assert.True(faithful.Covered >= StoryFidelityVerifier.MinCovered, $"covered {faithful.Covered}");
        var repaired = faithful.Units.Single(u => u.Path == "d");
        Assert.Equal((5, 0), (repaired.Words, repaired.Kept));
        Assert.Equal(2, faithful.Units.Single(u => u.Path == "c").Kept);

        var rewritten = TextOverlap.Measure(Manuscript,
        [
            new TextUnit("a", "Mara enters her tiny yellow kitchen, exhausted, and turns on the old radio."),
            new TextUnit("b", "Get out"),
        ]);

        Assert.True(rewritten.Kept < StoryFidelityVerifier.MinKept, $"kept {rewritten.Kept}");
        Assert.True(rewritten.Covered < StoryFidelityVerifier.MinCovered, $"covered {rewritten.Covered}");
        var missing = rewritten.Missing[0];
        Assert.Equal((51, "she said to the cat, slamming the cupboard door. Nothing happened, which…"), (missing.Words, missing.Snippet));
    }

    [Fact]
    public async Task A_rewritten_story_fails_naming_the_elements_and_the_dropped_passages()
    {
        var input = await InputAsync();
        var verdict = await VerifyAsync(input, [input], """
            { "title": "The Quiet Year", "characters": [{ "name": "Mara" }], "locations": [], "containers": [{ "label": "Act I", "scenes": [{
              "title": "Morning", "elements": [
                { "type": "Heading", "location": "Kitchen", "timeOfDay": "DAY" },
                { "type": "Action", "text": "Mara enters her tiny yellow kitchen, exhausted, and turns on the old radio." },
                { "type": "Dialogue", "speaker": "Mara", "text": "Get out" }
              ] }] }] }
            """);

        Assert.Equal(Verdict.Fail, verdict.Verdict);
        Assert.Contains("at least 80% and 60% are required", verdict.Reason);
        Assert.Contains(verdict.Findings, f =>
            f.Path == "$.containers[0].scenes[0].elements[1].text" && f.Message.StartsWith("13 of 13 words are not the manuscript's"));
        Assert.Contains(verdict.Findings, f => f.Path == ArtifactRef.InputStageId && f.Message.StartsWith("51 manuscript words are not in the story: \"she said to the cat,"));
    }

    [Fact]
    public async Task A_faithful_story_passes_when_the_manuscript_reached_it_through_an_earlier_stage()
    {
        var input = await InputAsync();
        var ledger = _services.GetRequiredService<IRunLedger>();
        var signature = new DocumentSignature(SimpleDocumentClassifier.ProseFamily, new Dictionary<string, string>());
        await ledger.OpenAsync(input.RunId, signature, RunMode.Discovery, Ct);
        var scenes = await _services.GetRequiredService<IArtifactStore>()
            .PutAsync(input.RunId, "scenes", Stream("[]"), new ContractRef("scenes", 1), Ct);
        await ledger.RecordAsync(new StageRecord(
            input.RunId,
            new StageDefinition("scenes", [], StoryStages.SourceContract, new ContractRef("scenes", 1), [], Budget.Unbounded),
            [input], new ExecutorRef("agent", 0), Tier.Orchestrator,
            new StageResult(scenes, null, Cost.Zero, []), [], IsShadow: false), Ct);

        var verdict = await VerifyAsync(input, [scenes], """
            { "title": "The Quiet Year", "characters": [{ "name": "Mara" }], "locations": [], "containers": [{ "label": "Act I", "scenes": [{
              "title": "Morning", "elements": [
                { "type": "Heading", "location": "Kitchen", "timeOfDay": "DAY" },
                { "type": "Description", "text": "The kitchen was small and yellow, and the radio on the sill had not worked in years." },
                { "type": "Action", "text": "Mara shuffled in, bleary, and flicked it on anyway." },
                { "type": "Dialogue", "speaker": "Mara", "parenthetical": "to the cat", "text": "Get out" },
                { "type": "Action", "text": "She slams the cupboard door." },
                { "type": "Narration", "text": "Nothing happened, which was how most mornings went in that house." },
                { "type": "Narration", "text": "Later she found the letter under the bread bin, three pages in her mother's hand, and read it standing up because sitting down would have made it real." }
              ] }] }] }
            """);

        Assert.Equal((Verdict.Pass, 0), (verdict.Verdict, verdict.Findings.Count));
        Assert.Matches(@"^\d+% of the story's words are the manuscript's and \d+% of the manuscript is in the story\.$", verdict.Reason);
    }

    private Task<ArtifactRef> InputAsync() =>
        _services.GetRequiredService<IArtifactStore>().PutAsync(
            Core.DocumentImportIds.New(), ArtifactRef.InputStageId, Stream(Manuscript), StoryStages.SourceContract, Ct);

    private async Task<VerifierResult> VerifyAsync(ArtifactRef input, IReadOnlyList<ArtifactRef> inputs, string story)
    {
        var artifacts = _services.GetRequiredService<IArtifactStore>();
        var output = await artifacts.PutAsync(input.RunId, StoryStages.StoryStage, Stream(story), StoryStages.StoryContract, Ct);
        var stage = new StageDefinition(
            StoryStages.StoryStage, [], StoryStages.SourceContract, StoryStages.StoryContract, [StoryFidelityVerifier.VerifierId], Budget.Unbounded);
        var request = new StageRequest(
            input.RunId, stage, new DocumentSignature(SimpleDocumentClassifier.ProseFamily, new Dictionary<string, string>()), inputs);
        var verifier = _services.GetServices<IVerifier>().Single(v => v.Id == StoryFidelityVerifier.VerifierId);
        return await verifier.VerifyAsync(request, new StageResult(output, null, Cost.Zero, []), Ct);
    }

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
