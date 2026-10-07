using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <summary>
/// A model-driven loop with two tools, as a coding harness has: <c>execute_skill</c> loads a skill's
/// instructions and <c>execute_code</c> runs one of its scripts. The engine's primitives are
/// built-in skills, and the agent writes its own skills and code as it learns a document family.
/// The conversation is journaled as it grows, so a run interrupted by an outage resumes from it.
/// </summary>
public sealed class DiscoveryAgent(
    ILanguageModelFactory models,
    SkillRuntimeFactory skills,
    DiscoveryGoal goal,
    DiscoveryAgentOptions options,
    TimeProvider time,
    ILogger<DiscoveryAgent> logger) : IDiscoveryAgent
{
    public const string ExecuteSkill = "execute_skill";
    public const string ExecuteCode = "execute_code";

    // A model that stops short of the deliverable is told so this many times before the run fails.
    private const int MaxNudges = 2;

    public static readonly IReadOnlyList<ToolDefinition> Tools =
    [
        new(ExecuteSkill,
            "Load a skill: returns its instructions and its scripts. Load a skill before you first run its scripts.",
            Schema("""
                {
                  "type": "object",
                  "properties": { "skill": { "type": "string", "description": "The skill's name." } },
                  "required": ["skill"]
                }
                """)),
        new(ExecuteCode,
            "Run one of a skill's scripts with JSON args. Returns the script's JSON result, or an error to fix.",
            Schema("""
                {
                  "type": "object",
                  "properties": {
                    "skill": { "type": "string", "description": "The skill the script belongs to." },
                    "script": { "type": "string", "description": "The script's name." },
                    "args": { "type": "object", "description": "The script's arguments, as its skill documents them." }
                  },
                  "required": ["skill", "script"]
                }
                """)),
    ];

    private static readonly JsonElement NoArgs = JsonSerializer.SerializeToElement(new { });

    public async Task RunAsync(ArtifactRef input, IAgentTools tools, TraceRef trace, CancellationToken ct)
    {
        // An id the family already has keeps its first definition; redefining it is refused.
        var defined = (await tools.Context()).Verifiers.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var verifier in goal.Verifiers.Where(v => !defined.Contains(v.Id)))
        {
            await tools.DefineVerifier(verifier);
        }

        var length = (await ReadAsync(tools, input, ct)).Length;
        var task = $"""
            The input document is this artifact ({length:N0} characters):

            {SkillJson.Serialize(input)}

            Deliver stage `{goal.StageId}` as the brief describes.
            """;
        await LoopAsync(tools, goal.StageId, task, ct);
    }

    public Task RunStageAsync(StageRequest request, IAgentTools tools, TraceRef trace, CancellationToken ct)
    {
        var task = $"""
            This run is scoped to one stage of a workflow mined from earlier runs. Produce stage
            `{request.Stage.Id}` with contract {SkillJson.Serialize(request.Stage.Output)} from these inputs:

            {SkillJson.Serialize(request.Inputs)}

            Only this stage can be written or delegated. The brief describes where the workflow ends.
            """;
        return LoopAsync(tools, request.Stage.Id, task, ct);
    }

    private async Task LoopAsync(IAgentTools tools, string deliverable, string task, CancellationToken ct)
    {
        var runtime = skills.Create(tools, ct);
        var system = SystemPrompt(await runtime.ListAsync());
        var model = models.For(options.ModelClass);
        var transcript = await Transcript.OpenAsync(tools, task);
        await tools.RecordSystemPrompt(transcript.Resumed ? transcript.Messages.Count : 0, system);
        if (transcript.Resumed)
        {
            await RestoreAsync(runtime, tools, transcript);
        }

        while (true)
        {
            if (transcript.PendingCalls is { Count: > 0 } pending)
            {
                var results = new List<ToolResult>(pending.Count);
                foreach (var call in pending)
                {
                    results.Add(await ExecuteAsync(runtime, call));
                }

                await transcript.AddAsync(ChatMessage.Results(results));
            }

            if (transcript.Turns >= options.MaxTurns)
            {
                throw new DiscoveryFailedException(
                    $"The discovery agent used its {options.MaxTurns} turns without a passing '{deliverable}' stage.");
            }

            var started = time.GetTimestamp();
            LanguageModelReply reply;
            try
            {
                reply = await model.ConverseAsync(system, transcript.Messages, Tools, ct);
            }
            catch (LanguageModelRefusedException e)
            {
                throw new DiscoveryFailedException($"The discovery agent's model refused the run: {e.Message}", e);
            }

            var spent = new Cost(reply.Cost, time.GetElapsedTime(started));
            runtime.Spend(spent);

            var calls = reply.ToolCalls ?? [];
            await transcript.AddAsync(ChatMessage.Assistant(reply.Text, calls), spent);
            if (calls.Count > 0)
            {
                continue;
            }

            if (runtime.HasPassed(deliverable))
            {
                return;
            }

            if (transcript.Nudges >= MaxNudges)
            {
                throw new DiscoveryFailedException(
                    $"The discovery agent stopped without a passing '{deliverable}' stage after {transcript.Messages.Count} messages.");
            }

            await transcript.AddAsync(ChatMessage.User(Transcript.Nudge(deliverable)));
        }
    }

    /// <summary>
    /// Puts the runtime where the interrupted loop left it: which stages have passed, and the model
    /// spend no write has carried yet (everything journaled less what the run's own records took).
    /// </summary>
    private async Task RestoreAsync(SkillRuntime runtime, IAgentTools tools, Transcript transcript)
    {
        var records = await tools.Records();
        foreach (var record in records)
        {
            runtime.Recorded(record.StageId, record.Passed);
        }

        var carried = records
            .Where(r => r.Tier == Tier.Orchestrator)
            .Aggregate(Cost.Zero, (sum, r) => new Cost(sum.Amount + r.Result.Cost.Amount, sum.Duration + r.Result.Cost.Duration));
        var undrained = new Cost(
            Math.Max(0, transcript.Spent.Amount - carried.Amount),
            transcript.Spent.Duration > carried.Duration ? transcript.Spent.Duration - carried.Duration : TimeSpan.Zero);
        runtime.Spend(undrained);

        logger.LogInformation(
            "Resumed the discovery loop at turn {Turn} with {Records} stage records and {Pending} pending tool calls",
            transcript.Turns, records.Count, transcript.PendingCalls.Count);
    }

    private async Task<ToolResult> ExecuteAsync(SkillRuntime runtime, ToolCall call)
    {
        var skill = Text(call.Input, "skill");
        var script = Text(call.Input, "script");
        var result = (call.Name, skill, script) switch
        {
            (ExecuteSkill, { } name, _) => await runtime.LoadAsync(name),
            (ExecuteCode, { } name, { } scriptName) => await runtime.RunAsync(
                name, scriptName, call.Input.TryGetProperty("args", out var args) ? args : NoArgs),
            (ExecuteSkill or ExecuteCode, _, _) => SkillResult.Error("'skill', and for execute_code 'script', are required."),
            _ => SkillResult.Error($"There is no tool '{call.Name}'."),
        };

        logger.LogInformation(
            "{Tool} {Skill}/{Script}: {Outcome}", call.Name, skill, script, result.IsError ? result.Content : "ok");

        var content = result.Content.Length > options.MaxResultChars
            ? result.Content[..options.MaxResultChars] + "\n[cut: the result is longer; read it in pages]"
            : result.Content;
        return new ToolResult(call.Id, call.Name, content, result.IsError);
    }

    private string SystemPrompt(IReadOnlyList<SkillSummary> available)
    {
        var prompt = new StringBuilder("""
            You are the discovery agent of a document import engine. You turn one input document into the
            deliverable in the brief below, and you get better at it across runs by keeping what you learn
            as skills and code.

            You have two tools. `execute_skill` loads a skill's instructions and lists its scripts.
            `execute_code` runs one of a skill's scripts with JSON args. Everything goes through skills: the
            engine's primitives are built-in skills, and the rest are skills you wrote in earlier runs of
            this document family. Load a skill before you first run its scripts.

            How to work:
            1. Run `context` first, and load any of your own skills whose description fits.
            2. Work in stages. Each stage turns artifacts into one artifact through `write-artifact` or
               `delegate`, naming exactly the inputs it used. Reuse the stage ids and executors earlier
               runs used.
            3. Do deterministic work - splitting, counting, reshaping, merging - in code (`create-code`),
               not by hand. Hand model work to executors (`define-playbook`, `define-executor`, `delegate`)
               at the smallest tier that passes.
            4. When a stage restructures text that already exists, move that text, don't regenerate it.
               Models decide boundaries and labels and reply with references into the source (unit
               numbers, offsets, anchors); code cuts the source's own characters and places them.
            5. Before you finish, keep what you learned with `create-skill` and `create-code`, so the next
               run of this family is cheaper. Update a skill rather than add a near-duplicate.
            6. You are done when the deliverable stage has passed its verifiers. Then stop calling tools.

            Skills:

            """);
        foreach (var skill in available)
        {
            prompt.AppendLine($"- {skill.Id}{(skill.BuiltIn ? "" : " (yours)")}: {skill.Description}");
        }

        return prompt.AppendLine().AppendLine("# Brief").AppendLine().Append(goal.Brief).ToString();
    }

    private static async Task<string> ReadAsync(IAgentTools tools, ArtifactRef artifact, CancellationToken ct) =>
        await ArtifactText.ReadAsync(await tools.ReadArtifact(artifact), ct);

    private static string? Text(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement Schema(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
