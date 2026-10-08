using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.UnitTests.Engine.Fakes;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public partial class DiscoveryAgentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The classifier is a model of its own; these tests are about the loop, so the family is fixed.
    private const string ProseFamily = "prose";

    // Deterministic on purpose: the test is about the loop, not the screenplay.
    private const string DraftScript = """
        public static class Script
        {
            public static async Task<object?> RunAsync(ScriptContext context)
            {
                var source = context.Arg<ArtifactRef>("source");
                var lines = (await context.ReadTextAsync(source)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var story = new
                {
                    title = lines[0],
                    characters = new[] { new { name = "Mara", kind = "Human" } },
                    locations = new[] { new { name = "Kitchen", setting = "Interior" } },
                    props = Array.Empty<object>(),
                    containers = new[]
                    {
                        new
                        {
                            label = "Act I",
                            scenes = new[]
                            {
                                new
                                {
                                    title = "Breakfast",
                                    elements = new object[]
                                    {
                                        new { type = "Heading", location = "Kitchen", timeOfDay = "DAY" },
                                        new { type = "Action", text = lines[^1] },
                                    },
                                },
                            },
                        },
                    },
                };

                return await context.RunAsync("write-artifact", "write-artifact", new
                {
                    stageId = "story",
                    contract = new { schemaId = "story-draft", version = 1 },
                    content = JsonSerializer.Serialize(story),
                    inputs = new[] { source },
                });
            }
        }
        """;

    private readonly ScriptedLanguageModelFactory _models = new();
    private readonly ServiceProvider _services;

    public DiscoveryAgentTests()
    {
        _services = Build(_models);
    }

    private static ServiceProvider Build(ScriptedLanguageModelFactory models, Action<DiscoveryAgentOptions>? configureAgent = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentWorkflowEngine();
        services.AddInMemoryWorkflowEngineStores();
        services.AddScreenplayDiscovery(configureAgent: configureAgent);
        services.AddRunBriefing();
        services.AddSingleton<ILanguageModelFactory>(models);
        services.AddSingleton<IDocumentClassifier>(new FixedDocumentClassifier(ProseFamily));
        return services.BuildServiceProvider();
    }

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    private Task<ArtifactRef> InputAsync(string text) =>
        Get<IArtifactStore>().PutAsync(
            Core.DocumentImportIds.New(), ArtifactRef.InputStageId, new MemoryStream(Encoding.UTF8.GetBytes(text)), StoryStages.SourceContract, Ct);

    private static ChatMessage Call(string tool, object input) =>
        ChatMessage.Assistant(null, [new ToolCall($"call_{Guid.NewGuid():N}", tool, SkillJson.ToElement(input))]);

    private static ChatMessage Done() => ChatMessage.Assistant("Done.", []);

    private static JsonElement InputOf(IReadOnlyList<ChatMessage> messages) =>
        JsonDocument.Parse(ArtifactJson().Match(messages[0].Text!).Value).RootElement;

    private IEnumerable<ToolResult> Results() => _models.Turns.Last().Messages.SelectMany(m => m.ToolResults);

    private static ChatMessage CreateDraftSkill() => Call(DiscoveryAgent.ExecuteCode, new
    {
        skill = "create-skill", script = "create",
        args = new { name = "draft-story", description = "Drafts a story from short prose.", instructions = "Run `draft` with { source }." },
    });

    private static ChatMessage CreateDraftCode() => Call(DiscoveryAgent.ExecuteCode, new
    {
        skill = "create-code", script = "create",
        args = new { skill = "draft-story", script = "draft", description = "{ source } -> writes stage story", source = DraftScript },
    });

    private static ChatMessage Draft(IReadOnlyList<ChatMessage> messages) =>
        Call(DiscoveryAgent.ExecuteCode, new { skill = "draft-story", script = "draft", args = new { source = InputOf(messages) } });

    private async Task<string> RunIdOfAsync(ArtifactRef input) =>
        (await Get<IRunLedger>().ProgressAsync([input.RunId], Ct))[input.RunId].RunId!;

    [Fact]
    public async Task The_agent_works_through_two_tools_and_keeps_the_code_it_writes()
    {
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }),
            _ => CreateDraftSkill(),
            _ => CreateDraftCode(),
            Draft,
            _ => Done());

        var summary = await Get<RunDispatcher>().RunAsync(await InputAsync("The Quiet Year\n\nMara turns the radio on."), Ct);

        Assert.All(Results(), r => Assert.False(r.IsError, r.Content));
        var story = Assert.Single(summary.Stages);
        Assert.Equal(
            (StoryStages.StoryStage, Tier.Orchestrator, true), (story.StageId, story.Tier, story.Passed));
        Assert.Equal(0.04m, story.Result.Cost.Amount);
        var draft = StoryJson.Deserialize<StoryDraft>(await ArtifactText.ReadAsync(Get<IArtifactStore>(), story.Output, Ct));
        Assert.Equal("The Quiet Year", draft.Title);

        var (system, _) = _models.Turns.First();
        Assert.Contains("- create-skill:", system);
        Assert.Contains("- create-code:", system);
        Assert.Contains("# Brief", system);

        _models.ScriptTurns(ModelClasses.Medium, _ => Done());
        var next = await InputAsync("Again.");
        await Assert.ThrowsAsync<DiscoveryFailedException>(() => Get<RunDispatcher>().RunAsync(next, Ct));
        Assert.Contains("- draft-story (yours): Drafts a story from short prose.", _models.Turns.Last().System);
    }

    [Fact]
    public async Task Each_turn_is_recorded_as_a_step_with_what_its_calls_did_scripts_included()
    {
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }),
            _ => CreateDraftSkill(),
            _ => CreateDraftCode(),
            Draft,
            _ => Done());

        var summary = await Get<RunDispatcher>().RunAsync(await InputAsync("The Quiet Year\n\nMara turns the radio on."), Ct);

        var steps = await Get<IRunLedger>().StepsAsync(summary.RunId, Ct);
        Assert.Equal([1, 3, 5, 7], steps.Select(s => s.Sequence));
        Assert.Equal("Loaded built-in context", steps[0].Headline);
        Assert.Equal("Published draft-story@1 (new skill)", steps[1].Headline);
        Assert.StartsWith("Published draft-story@2: new script `draft` (", steps[2].Headline);

        var draft = Assert.Single(steps[3].Calls);
        Assert.Equal((DiscoveryAgent.ExecuteCode, "draft-story", "draft", false), (draft.Tool, draft.Skill, draft.Script, draft.IsError));
        Assert.Collection(
            draft.Effects,
            e => Assert.Equal((StepEffectKind.ScriptRan, "Ran draft-story@2/draft", 0, "draft-story@2"), (e.Kind, e.Summary, e.Depth, e.Subject)),
            e => Assert.Equal((StepEffectKind.ArtifactWritten, "Wrote stage `story`: passed", 1), (e.Kind, e.Summary, e.Depth)));
    }

    [Fact]
    public async Task A_family_whose_story_verifier_was_defined_differently_still_runs()
    {
        await Get<IDocumentFamilyCatalog>().AddVerifierAsync(
            ProseFamily,
            new VerifierSpec(StoryGoal.Goal.Verifiers[0].Id, StoryStages.StoryStage, "The story has a title and at least one scene."),
            Ct);
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }),
            _ => Done());

        var failed = await Assert.ThrowsAsync<DiscoveryFailedException>(
            async () => await Get<RunDispatcher>().RunAsync(await InputAsync("A short tale."), Ct));

        Assert.Contains("without a passing 'story' stage", failed.Message);
    }

    [Fact]
    public async Task A_rubric_verifier_the_agent_defines_is_judged_by_a_model()
    {
        _models.Script(
            ModelClasses.Medium,
            (_, _) => """{ "verdict": "Fail", "reason": "The ending is missing.", "findings": [{ "path": "$.containers", "message": "No final scene." }] }""",
            (_, _) => """
                ```json
                { "verdict": "Pass", "reason": "Every beat is covered." }
                ```
                """);
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteCode, new
            {
                skill = "define-verifier", script = "define-verifier",
                args = new { id = "story-covers-manuscript", stageId = "story", rubric = "Every beat of the manuscript appears in the story." },
            }),
            _ => CreateDraftSkill(),
            _ => CreateDraftCode(),
            Draft,
            Draft,
            _ => Done());

        var summary = await Get<RunDispatcher>().RunAsync(await InputAsync("The Quiet Year\n\nMara turns the radio on."), Ct);

        Assert.All(Results(), r => Assert.False(r.IsError, r.Content));
        Assert.Contains(Results(), r => r.Content.Contains("The ending is missing."));
        var verdict = summary.Stages.Last().Verdicts.Single(v => v.VerifierId == "story-covers-manuscript");
        Assert.Equal((Verdict.Pass, "Every beat is covered."), (verdict.Verdict, verdict.Reason));

        var (modelClass, _, user) = _models.Calls.First();
        Assert.Equal(ModelClasses.Medium, modelClass);
        Assert.Contains("Every beat of the manuscript appears in the story.", user);
        Assert.Contains("<manuscript>\nThe Quiet Year", user);
        Assert.Contains("<output>", user);
    }

    [Fact]
    public async Task Tool_errors_go_back_to_the_model_and_stopping_short_fails_the_run()
    {
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "no-such-skill" }),
            _ => Done());

        await Assert.ThrowsAsync<DiscoveryFailedException>(
            async () => await Get<RunDispatcher>().RunAsync(await InputAsync("A short tale."), Ct));

        var error = Assert.Single(Results());
        Assert.True(error.IsError);
        Assert.Contains("There is no skill 'no-such-skill'.", error.Content);
        var nudges = _models.Turns.Last().Messages.Count(m => m.Text?.Contains("has not passed its verifiers") == true);
        Assert.Equal(2, nudges);
    }

    [Fact]
    public async Task A_run_cut_off_by_a_model_outage_resumes_its_conversation_on_the_next_attempt()
    {
        var outage = new LanguageModelUnavailableException("scripted", 5, TimeSpan.FromMinutes(10), new HttpRequestException("down"));
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }),
            _ => CreateDraftSkill(),
            _ => CreateDraftCode(),
            _ => throw outage,
            Draft,
            _ => Done());
        var input = await InputAsync("The Quiet Year\n\nMara turns the radio on.");

        var cutOff = await Assert.ThrowsAsync<LanguageModelUnavailableException>(() => Get<RunDispatcher>().RunAsync(input, Ct));
        Assert.Same(outage, cutOff);
        var runId = await RunIdOfAsync(input);

        var summary = await Get<RunDispatcher>().RunAsync(input, Ct);

        Assert.Equal(runId, summary.RunId);
        Assert.All(Results(), r => Assert.False(r.IsError, r.Content));
        var story = Assert.Single(summary.Stages);
        Assert.Equal((StoryStages.StoryStage, true), (story.StageId, story.Passed));

        // The skill and its code were written once; the resumed model saw the whole earlier conversation.
        Assert.Equal(6, _models.Turns.Count);
        var resumed = _models.Turns.ElementAt(4).Messages;
        Assert.Equal(7, resumed.Count);
        Assert.Equal(["create-skill", "create-code"], resumed.Skip(3).Where(m => m.ToolCalls.Count > 0).Select(m => m.ToolCalls[0].Input.GetProperty("skill").GetString()));
        var versions = (await Get<IDocumentFamilyCatalog>().SkillsAsync(ProseFamily, Ct)).Where(s => s.Id == "draft-story");
        Assert.Equal(2, versions.Max(s => s.Version));

        // The three turns before the outage are still charged to the story.
        Assert.Equal(0.04m, story.Result.Cost.Amount);

        // The resumed prompt lists the skill the first attempt wrote, so it is kept beside the first.
        var prompts = await Get<IRunLedger>().SystemPromptsAsync(runId, Ct);
        Assert.Equal([0, 7], prompts.Select(p => p.FromSequence));
        Assert.Equal(_models.Turns.First().System, prompts[0].Prompt);
        Assert.Equal(_models.Turns.ElementAt(4).System, prompts[1].Prompt);
        Assert.DoesNotContain("draft-story (yours)", prompts[0].Prompt);
        Assert.Contains("draft-story (yours)", prompts[1].Prompt);
    }

    [Fact]
    public async Task A_tool_call_cut_off_before_its_result_is_run_again_on_resume()
    {
        using var cutOff = new CancellationTokenSource();
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }),
            _ => CreateDraftSkill(),
            _ => CreateDraftCode(),
            messages =>
            {
                cutOff.Cancel();
                return Draft(messages);
            },
            _ => Done());
        var input = await InputAsync("The Quiet Year\n\nMara turns the radio on.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Get<RunDispatcher>().RunAsync(input, cutOff.Token));
        var runId = await RunIdOfAsync(input);

        var summary = await Get<RunDispatcher>().RunAsync(input, Ct);

        Assert.Equal(runId, summary.RunId);
        var story = Assert.Single(summary.Stages);
        Assert.True(story.Passed);
        Assert.Equal(5, _models.Turns.Count);
        var resumed = _models.Turns.Last().Messages;
        Assert.Equal(9, resumed.Count);
        var pending = resumed[7].ToolCalls.Single();
        Assert.Equal(pending.Id, resumed[8].ToolResults.Single().CallId);
    }

    [Fact]
    public async Task A_run_that_spends_its_turns_is_abandoned_and_the_next_attempt_starts_over()
    {
        var models = new ScriptedLanguageModelFactory();
        await using var services = Build(models, agent => agent.MaxTurns = 2);
        models.ScriptTurns(ModelClasses.Medium, _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }));
        var input = await services.GetRequiredService<IArtifactStore>().PutAsync(
            Core.DocumentImportIds.New(), ArtifactRef.InputStageId, new MemoryStream("A tale."u8.ToArray()), StoryStages.SourceContract, Ct);
        var ledger = services.GetRequiredService<IRunLedger>();

        var failed = await Assert.ThrowsAsync<DiscoveryFailedException>(() => services.GetRequiredService<RunDispatcher>().RunAsync(input, Ct));
        Assert.Contains("used its 2 turns", failed.Message);
        var abandoned = (await ledger.ProgressAsync([input.RunId], Ct))[input.RunId].RunId!;
        Assert.Null(await ledger.FindOpenAsync(abandoned, Ct));

        await Assert.ThrowsAsync<DiscoveryFailedException>(() => services.GetRequiredService<RunDispatcher>().RunAsync(input, Ct));

        Assert.NotEqual(abandoned, (await ledger.ProgressAsync([input.RunId], Ct))[input.RunId].RunId);
        Assert.Single(models.Turns.ElementAt(2).Messages);
    }

    [Fact]
    public async Task A_run_whose_journal_cannot_be_read_is_abandoned()
    {
        _models.ScriptTurns(ModelClasses.Medium, _ => throw new IOException("the network went away"));
        var input = await InputAsync("A tale.");
        await Assert.ThrowsAsync<IOException>(() => Get<RunDispatcher>().RunAsync(input, Ct));
        var runId = await RunIdOfAsync(input);
        await Get<IRunLedger>().AppendTranscriptAsync(runId, 1, "not json", Ct);

        var failed = await Assert.ThrowsAsync<DiscoveryFailedException>(() => Get<RunDispatcher>().RunAsync(input, Ct));

        Assert.Contains("journal could not be read", failed.Message);
        Assert.Null(await Get<IRunLedger>().FindOpenAsync(runId, Ct));
    }

    [GeneratedRegex(@"\{""runId"".*?\}")]
    private static partial Regex ArtifactJson();
}
