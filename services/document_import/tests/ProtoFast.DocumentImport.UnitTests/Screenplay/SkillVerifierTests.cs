using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.Screenplay.Verifiers;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public class SkillVerifierTests
{
    private const string Family = "tv-episode-screenplays";

    private const string Manuscript = """
        DYING FOR SEX
        "Good Value Diet Soda"

        INT. HOSPITAL - DAY

        MOLLY sits on the bed. A nurse checks her chart.

                            MOLLY
                  I'm fine.

                            NURSE
                  You're not.
        """;

    private const string Story = """
        { "title": "Dying for Sex", "characters": [{ "name": "MOLLY" }, { "name": "NURSE" }], "locations": [], "containers": [] }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ScriptedLanguageModelFactory _models = new();
    private readonly ServiceProvider _services;

    public SkillVerifierTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentWorkflowEngine();
        services.AddInMemoryWorkflowEngineStores();
        services.AddScreenplayDiscovery();
        services.AddSkillVerification();
        services.AddSingleton<ILanguageModelFactory>(_models);
        _services = services.BuildServiceProvider();
    }

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    private ISkillVerifier Verifier(string id) => _services.GetServices<ISkillVerifier>().Single(v => v.Id == id);

    [Theory]
    [InlineData("""var speakers = new Dictionary<string, string> { ["I'm fine."] = "MOLLY" };""", "names MOLLY, a character of \"Dying for Sex\"")]
    [InlineData("""// import-dying-for-sex""", "names the title \"Dying for Sex\"")]
    public async Task A_skill_naming_an_imported_work_fails(string line, string finding)
    {
        await ImportedAsync();
        var review = await ReviewAsync("Cues are capitalized lines.", ("import", line));

        var verdict = await Verifier(SkillNamesVerifier.VerifierId).VerifyAsync(review, Ct);

        Assert.Equal(Verdict.Fail, verdict.Verdict);
        Assert.Equal(new Finding("scripts/import", finding), Assert.Single(verdict.Findings));
        Assert.Contains("A skill serves every document of the family", verdict.Reason);
    }

    [Fact]
    public async Task Names_the_manuscript_also_uses_as_ordinary_words_pass()
    {
        await ImportedAsync();
        var review = await ReviewAsync("A NURSE cue is a speaker like any other; sluglines start INT. or EXT.", ("import", "return null;"));

        var verdict = await Verifier(SkillNamesVerifier.VerifierId).VerifyAsync(review, Ct);

        Assert.Equal((Verdict.Pass, 0), (verdict.Verdict, verdict.Findings.Count));
    }

    [Fact]
    public async Task The_judge_reads_the_whole_skill_and_the_document_in_hand()
    {
        _models.Script(ModelClasses.Medium, (_, _) => """
            { "verdict": "Fail", "reason": "The orphan map only fits one episode.", "findings": [{ "path": "scripts/import", "message": "keyed by one episode's lines" }] }
            """);
        var review = await ReviewAsync("Run `import`.", ("import", """var orphans = new[] { "Where are we going?" };"""));

        var verdict = await Verifier(SkillGeneralityVerifier.VerifierId).VerifyAsync(review, Ct);

        Assert.Equal((Verdict.Fail, "The orphan map only fits one episode."), (verdict.Verdict, verdict.Reason));
        Assert.Equal(new Finding("scripts/import", "keyed by one episode's lines"), Assert.Single(verdict.Findings));
        var (_, _, user) = Assert.Single(_models.Calls);
        Assert.Contains($"<family>{Family}</family>", user);
        Assert.Contains("synopsis: Molly learns her cancer is back.", user);
        Assert.Contains("Run `import`.", user);
        Assert.Contains("""<script name="import" description="imports">""", user);
        Assert.Contains("Where are we going?", user);
    }

    /// <summary>A closed run of the family whose story passed.</summary>
    private async Task ImportedAsync()
    {
        var artifacts = Get<IArtifactStore>();
        var ledger = Get<IRunLedger>();
        var input = await artifacts.PutAsync(
            Core.DocumentImportIds.New(), ArtifactRef.InputStageId, Stream(Manuscript), StoryStages.SourceContract, Ct);
        await ledger.OpenAsync(input.RunId, Signature(), RunMode.Discovery, Ct);
        var story = await artifacts.PutAsync(input.RunId, StoryStages.StoryStage, Stream(Story), StoryStages.StoryContract, Ct);
        await ledger.RecordAsync(new StageRecord(
            input.RunId,
            new StageDefinition(StoryStages.StoryStage, [], StoryStages.SourceContract, StoryStages.StoryContract, [], Budget.Unbounded),
            [input], new ExecutorRef("agent", 0), Tier.Orchestrator,
            new StageResult(story, null, Cost.Zero, []), [], IsShadow: false), Ct);
        await ledger.CloseAsync(input.RunId, null, Ct);
    }

    /// <summary>A skill published from a new run of the family, before its own story exists.</summary>
    private async Task<SkillReview> ReviewAsync(string instructions, (string Name, string Source) script)
    {
        var input = await Get<IArtifactStore>().PutAsync(
            Core.DocumentImportIds.New(), ArtifactRef.InputStageId, Stream("INT. BAR - NIGHT"), StoryStages.SourceContract, Ct);
        await Get<IRunLedger>().OpenAsync(input.RunId, Signature(), RunMode.Discovery, Ct);
        var skill = new Skill(
            new SkillRef("screenplay-story-importer", 3), "Import a screenplay.", instructions,
            [new SkillScript(script.Name, "imports", "hash")]);
        return new SkillReview(
            skill, new Dictionary<string, string> { [script.Name] = script.Source }, input.RunId, Signature(), input);
    }

    private static DocumentSignature Signature() =>
        new(Family, new Dictionary<string, string> { ["synopsis"] = "Molly learns her cancer is back." });

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
