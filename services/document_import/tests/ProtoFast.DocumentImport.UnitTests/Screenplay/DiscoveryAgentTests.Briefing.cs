using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Briefing;
using ProtoFast.DocumentImport.Screenplay.Models;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

// The briefer reads what the loop journals, so its tests run real loops.
public partial class DiscoveryAgentTests
{
    private static readonly JsonSerializerOptions JournalJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_finished_run_is_briefed_with_its_skill_changes_in_view_and_only_unclear_steps_go_to_a_model()
    {
        _models.ScriptTurns(
            ModelClasses.Medium,
            _ => Call(DiscoveryAgent.ExecuteSkill, new { skill = "context" }),
            _ => CreateDraftSkill(),
            _ => CreateDraftCode(),
            Draft,
            _ => Done());
        var summary = await Get<RunDispatcher>().RunAsync(await InputAsync("The Quiet Year\n\nMara turns the radio on."), Ct);
        _models.Script(ModelClasses.Small, (_, _) => """
            {"steps": [
              {"sequence": 1, "text": "Not asked for."},
              {"sequence": 3, "text": "Started a draft-story skill."},
              {"sequence": 5, "text": "Wrote the draft script, which hardcodes Mara and the Kitchen."},
              {"sequence": 7, "text": "Ran it; the story passed."}
            ]}
            """);
        _models.Script(ModelClasses.Medium, (_, _) => """
            {"outcome": "Delivered the story on the first write.",
             "overview": "The agent wrote a draft-story skill and a script, then ran it.",
             "flags": [
               {"kind": "manuscript-specific-skill", "detail": "The draft script hardcodes the character Mara.", "sequence": 5},
               {"kind": "other", "detail": "Points at a step that does not exist.", "sequence": 99}
             ]}
            """);

        var brief = await Get<RunBriefer>().BriefAsync(
            new BriefCandidate(summary.RunId, summary.DocumentSignature.Family, RunMode.Discovery, RunStatus.Closed, null), Ct);

        var (_, _, steps) = Assert.Single(_models.Calls, c => c.ModelClass == ModelClasses.Small);
        Assert.Contains("Brief steps 3, 5, 7.", steps);
        Assert.Contains("New skill draft-story@1: Drafts a story from short prose.", steps);
        Assert.Contains("New script `draft`: { source } -> writes stage story", steps);
        Assert.Contains("+ public static class Script", steps);
        Assert.Contains("\"source\":\"(", steps);
        Assert.Contains("engine: Ran draft-story@2/draft; Wrote stage `story`: passed", steps);

        var (_, _, overview) = _models.Calls.Last();
        Assert.Contains("#1: Loaded built-in context", overview);
        Assert.Contains("#5: Published draft-story@2: new script `draft`", overview);
        Assert.Contains("    Ran it; the story passed.", overview);
        Assert.Contains("- story by ", overview);

        Assert.Equal("Delivered the story on the first write.", brief.Outcome);
        Assert.Equal([3, 5, 7], brief.Steps.Select(s => s.Sequence));
        Assert.Equal([5, null], brief.Flags.Select(f => f.Sequence));
        Assert.Equal(("scripted-medium", 0.02m), (brief.ModelId, brief.Cost));
    }

    [Fact]
    public async Task A_run_from_before_steps_were_recorded_gets_them_from_its_transcript()
    {
        var ledger = Get<IRunLedger>();
        const string runId = "run-before-steps";
        await ledger.OpenAsync(runId, new DocumentSignature(ProseFamily, new Dictionary<string, string>()), RunMode.Discovery, Ct);
        var call = new ToolCall("call_1", DiscoveryAgent.ExecuteCode, SkillJson.ToElement(new
        {
            skill = "create-skill", script = "create", args = new { name = "gone", description = "d", instructions = "i" },
        }));
        ChatMessage[] messages =
        [
            ChatMessage.User("Deliver stage `story`."),
            ChatMessage.Assistant("Keeping what I learned.", [call]),
            ChatMessage.Results([new ToolResult(call.Id, call.Name, """{"id":"gone","version":1}""", false)]),
            ChatMessage.Assistant("Done.", []),
        ];
        for (var i = 0; i < messages.Length; i++)
        {
            await ledger.AppendTranscriptAsync(runId, i, JsonSerializer.Serialize(new TranscriptEntry(messages[i], null), JournalJson), Ct);
        }

        await ledger.CloseAsync(runId, null, Ct);
        _models.Script(ModelClasses.Small, (_, _) => """{"steps": [{"sequence": 1, "text": "Published a skill."}]}""");
        _models.Script(ModelClasses.Medium, (_, _) => """{"outcome": "Did nothing useful.", "overview": "It published a skill and stopped.", "flags": []}""");

        var brief = await Get<RunBriefer>().BriefAsync(
            new BriefCandidate(runId, ProseFamily, RunMode.Discovery, RunStatus.Closed, null), Ct);

        var step = Assert.Single(await ledger.StepsAsync(runId, Ct));
        var recorded = Assert.Single(step.Calls);
        Assert.Equal((1, "create-skill", "create", false), (step.Sequence, recorded.Skill, recorded.Script, recorded.IsError));
        Assert.Empty(recorded.Effects);
        Assert.Equal("Ran create-skill/create", step.Headline);
        Assert.Contains("gone@1 is no longer in the registry.", _models.Calls.First().User);
        Assert.Contains("<final-message>\nDone.", _models.Calls.Last().User.ReplaceLineEndings("\n"));
        Assert.Equal("Published a skill.", Assert.Single(brief.Steps).Text);
    }
}
