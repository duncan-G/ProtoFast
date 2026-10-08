using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Briefing;

/// <summary>
/// Writes what a person reads instead of a finished run's conversation. A step whose recorded
/// effects say what happened keeps them as its headline; a step that ran the family's own code,
/// published a skill, or reasoned at length gets a model brief written with the steps around it in
/// view; and the run gets an outcome, an overview and flags for the reviewer.
/// </summary>
public sealed class RunBriefer(
    IRunLedger ledger,
    IRegistry registry,
    IDocumentFamilyCatalog catalog,
    ILanguageModelFactory models,
    RunBriefingOptions options,
    ILogger<RunBriefer> logger)
{
    private const string CreateSkill = "create-skill";
    private const string CreateCode = "create-code";
    private const int MaxHeadline = 400;
    private const int EarlierSteps = 40;

    private static readonly string[] BulkyArgs = ["instructions", "source"];

    private const string StepSystem = """
        You brief the steps of a document import agent's run for the engineer who reviews it. The agent
        works through two tools: loading a skill (its instructions and scripts) and running one of a
        skill's scripts. Built-in skills are the engine's primitives (context, read-artifact,
        write-artifact, delegate, create-skill, create-code and others); every other skill is one the
        agent wrote in an earlier run of this document family.

        Each step is one model turn: what the agent said, the scripts it ran and their arguments, what the
        engine saw them do, their results, and for a skill or script it published, the change against
        the version before.

        By design, code cuts the manuscript's own text into place and a script writes the artifact
        itself, so the text never passes through a model; that is the intended way to work, not a
        shortcut.

        Write a brief for each step you are asked about, in one to three plain sentences: what the agent
        did, why (from what it said or the steps before), and what came of it. Name skills, scripts,
        stages and verifiers exactly. When published code or instructions hardcode facts about one
        manuscript (its character, location or prop names, particular lines, one draft's page headers)
        instead of handling the family in general, say so. Add what the engine line leaves out rather
        than repeating it.

        Reply with one JSON object and nothing else:
        {"steps": [{"sequence": <the step number>, "text": "<the brief>"}]}
        """;

    private const string OverviewSystem = """
        You review a document import agent's finished run for the engineer who maintains it. You are
        given what the engine recorded about the run (its status, stage attempts and decisions) and a
        timeline of its steps: each step's engine line and, for some, a brief of what it did.

        Reply with one JSON object and nothing else:
        {
          "outcome": "<one sentence: whether the run delivered, and why not if it did not>",
          "overview": "<the path the run took in 80 to 200 words: what it tried, what failed and why, what it changed, how it reached the end>",
          "flags": [{"kind": "<manuscript-specific-skill | duplicate-skill | skill-regression | verifier-workaround | wasted-turns | repeated-failure | other>", "detail": "<one sentence a reviewer can act on>", "sequence": <the step it is about, or null>}]
        }

        Flag only what a reviewer should look at: a skill or script that hardcodes one manuscript's names,
        lines or layout instead of handling the family; a near-duplicate of a skill or script the family
        already has; a change that drops behaviour an earlier version had; output shaped to get past a
        verifier rather than to be right; a stretch of turns that made no progress; the same failure
        repeated. Code that cuts the manuscript's own text into place and a script that writes the
        artifact itself are the intended design, not something to flag. An empty list is a fine answer.
        """;

    public async Task<RunBrief> BriefAsync(BriefCandidate run, CancellationToken ct)
    {
        var summary = await ledger.SummariseAsync(run.RunId, ct);
        var messages = (await ledger.TranscriptAsync(run.RunId, ct)).Select(Read).ToList();
        var steps = await StepsAsync(run.RunId, messages, ct);

        var digests = new List<StepDigest>(steps.Count);
        foreach (var step in steps)
        {
            digests.Add(await DigestAsync(run.Family, step, messages, ct));
        }

        var header = Header(run, summary, messages);
        var cost = 0m;
        var briefs = new List<StepBrief>();
        foreach (var chunk in Chunks(digests))
        {
            var wanted = chunk.Where(d => d.NeedsBrief).Select(d => d.Step.Sequence).ToHashSet();
            if (wanted.Count == 0)
            {
                continue;
            }

            var reply = await models.For(options.StepModelClass)
                .CompleteAsync(StepSystem, StepPrompt(header, digests, chunk, wanted), ct);
            cost += reply.Cost;
            foreach (var brief in StoryJson.Deserialize<StepBriefsReply>(StoryJson.ExtractObject(reply.Text)).Steps ?? [])
            {
                if (!string.IsNullOrWhiteSpace(brief.Text) && wanted.Remove(brief.Sequence))
                {
                    briefs.Add(brief with { Text = brief.Text.Trim() });
                }
            }
        }

        var final = await models.For(options.OverviewModelClass)
            .CompleteAsync(OverviewSystem, OverviewPrompt(header, digests, briefs, messages), ct);
        cost += final.Cost;
        var overview = StoryJson.Deserialize<OverviewReply>(StoryJson.ExtractObject(final.Text));
        if (string.IsNullOrWhiteSpace(overview.Outcome) || string.IsNullOrWhiteSpace(overview.Overview))
        {
            throw new JsonException("The overview model returned no outcome or overview.");
        }

        var sequences = steps.Select(s => s.Sequence).ToHashSet();
        var flags = (overview.Flags ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.Kind) && !string.IsNullOrWhiteSpace(f.Detail))
            .Select(f => f with { Sequence = f.Sequence is { } s && sequences.Contains(s) ? s : null })
            .ToList();

        logger.LogInformation(
            "Briefed run {RunId}: {Steps} steps, {Briefs} model briefs, {Flags} flags, ${Cost:0.####}",
            run.RunId, steps.Count, briefs.Count, flags.Count, cost);
        return new RunBrief(
            overview.Outcome.Trim(), overview.Overview.Trim(), flags, briefs.OrderBy(b => b.Sequence).ToList(), final.ModelId, cost);
    }

    /// <summary>The run's recorded steps, recording any a run from before steps were kept is missing.</summary>
    private async Task<IReadOnlyList<RunStep>> StepsAsync(string runId, IReadOnlyList<ChatMessage?> messages, CancellationToken ct)
    {
        var recorded = (await ledger.StepsAsync(runId, ct)).ToDictionary(s => s.Sequence);
        var steps = new List<RunStep>();
        for (var sequence = 0; sequence < messages.Count; sequence++)
        {
            if (messages[sequence] is not { Role: ChatRole.Assistant, ToolCalls.Count: > 0 } turn)
            {
                continue;
            }

            if (!recorded.TryGetValue(sequence, out var step))
            {
                var results = ResultsOf(messages, sequence);
                if (results.Count == 0)
                {
                    // The run ended on calls it never made.
                    continue;
                }

                step = new RunStep(sequence, turn.ToolCalls
                    .Select(c => new StepCall(
                        c.Id, c.Name, Text(c.Input, "skill"), c.Name == DiscoveryAgent.ExecuteCode ? Text(c.Input, "script") : null,
                        results.FirstOrDefault(r => r.CallId == c.Id)?.IsError ?? false, []))
                    .ToList());
                await ledger.RecordStepAsync(runId, step, ct);
            }

            steps.Add(step);
        }

        return steps;
    }

    private async Task<StepDigest> DigestAsync(
        string family, RunStep step, IReadOnlyList<ChatMessage?> messages, CancellationToken ct)
    {
        var turn = messages[step.Sequence];
        var results = ResultsOf(messages, step.Sequence);
        var text = new StringBuilder().AppendLine($"### Step {step.Sequence}");

        var reasoning = turn?.Text?.Trim() ?? "";
        if (reasoning.Length > 0)
        {
            text.AppendLine($"Agent: {Clip(reasoning, options.MaxTextChars)}");
        }

        var needsBrief = reasoning.Length > options.ReasoningChars;
        foreach (var call in step.Calls)
        {
            var tool = turn?.ToolCalls.FirstOrDefault(c => c.Id == call.CallId);
            var result = results.FirstOrDefault(r => r.CallId == call.CallId);
            text.AppendLine(call.Script is null ? $"- load {call.Skill}" : $"- run {call.Skill}/{call.Script}");
            if (call.Script is not null && tool is not null)
            {
                text.AppendLine($"  args: {Clip(Args(tool.Input), options.MaxTextChars)}");
            }

            text.AppendLine($"  engine: {call.Headline}");
            if (call.Script is not null && result is not null)
            {
                text.AppendLine($"  result{(result.IsError ? " (error)" : "")}: {Clip(result.Content, options.MaxTextChars)}");
            }

            if (call.Skill is { } skill && call.Script is not null && !SkillRuntime.IsBuiltIn(skill))
            {
                needsBrief = true;
            }

            if (call.Skill is CreateSkill or CreateCode && !call.IsError && Published(call, result) is { } published)
            {
                needsBrief = true;
                text.AppendLine("  change:").AppendLine(Indent(await ChangeAsync(family, published, ct)));
            }
        }

        return new StepDigest(step, needsBrief, text.ToString());
    }

    private async Task<string> ChangeAsync(string family, SkillRef published, CancellationToken ct)
    {
        Skill current;
        try
        {
            current = await registry.ResolveAsync(published, ct);
        }
        catch (KeyNotFoundException)
        {
            return $"{published} is no longer in the registry.";
        }

        // The family's own previous version: skill ids are not unique across families.
        var earlier = (await catalog.SkillsAsync(family, ct))
            .Where(s => s.Id == published.Id && s.Version < published.Version)
            .OrderByDescending(s => s.Version)
            .Select(s => (SkillRef?)s)
            .FirstOrDefault();
        Skill? previous = null;
        if (earlier is { } reference)
        {
            try
            {
                previous = await registry.ResolveAsync(reference, ct);
            }
            catch (KeyNotFoundException)
            {
                // Its content is gone from the object store; treat the skill as new.
            }
        }

        var change = new StringBuilder();
        if (previous is null)
        {
            change.AppendLine($"New skill {published}: {current.Description}").AppendLine("Instructions:").AppendLine(current.Instructions);
        }
        else
        {
            change.AppendLine($"{published} against {previous.Ref}:");
            if (previous.Description != current.Description)
            {
                change.AppendLine($"Description was: {previous.Description}").AppendLine($"Description now: {current.Description}");
            }

            if (LineDiff.Unified(previous.Instructions, current.Instructions) is { Length: > 0 } instructions)
            {
                change.AppendLine("Instructions diff:").Append(instructions);
            }
        }

        foreach (var script in current.Scripts)
        {
            var before = previous?.Scripts.FirstOrDefault(s => s.Name == script.Name);
            if (before?.CodeHash == script.CodeHash)
            {
                continue;
            }

            var source = await CodeAsync(script.CodeHash, ct);
            change.AppendLine(before is null ? $"New script `{script.Name}`: {script.Description}" : $"Script `{script.Name}` diff:")
                .Append(LineDiff.Unified(before is null ? "" : await CodeAsync(before.CodeHash, ct), source));
        }

        foreach (var removed in previous?.Scripts.Where(s => current.Scripts.All(c => c.Name != s.Name)) ?? [])
        {
            change.AppendLine($"Script `{removed.Name}` removed.");
        }

        return ClipBlock(change.ToString(), options.MaxDiffChars);
    }

    private async Task<string> CodeAsync(string hash, CancellationToken ct)
    {
        try
        {
            await using var code = await registry.OpenCodeAsync(hash, ct);
            using var reader = new StreamReader(code);
            return await reader.ReadToEndAsync(ct);
        }
        catch (KeyNotFoundException)
        {
            return $"(code {hash} is missing from the object store)";
        }
    }

    private IEnumerable<IReadOnlyList<StepDigest>> Chunks(IReadOnlyList<StepDigest> digests)
    {
        var chunk = new List<StepDigest>();
        var size = 0;
        foreach (var digest in digests)
        {
            if (chunk.Count > 0 && size + digest.Text.Length > options.MaxChunkChars)
            {
                yield return chunk;
                chunk = [];
                size = 0;
            }

            chunk.Add(digest);
            size += digest.Text.Length;
        }

        if (chunk.Count > 0)
        {
            yield return chunk;
        }
    }

    private static string StepPrompt(
        string header, IReadOnlyList<StepDigest> all, IReadOnlyList<StepDigest> chunk, IReadOnlySet<int> wanted)
    {
        var first = chunk[0].Step.Sequence;
        var earlier = all.Where(d => d.Step.Sequence < first).TakeLast(EarlierSteps).ToList();
        var prompt = new StringBuilder().AppendLine("<run>").Append(header).AppendLine("</run>");
        if (earlier.Count > 0)
        {
            prompt.AppendLine("<earlier-steps>");
            foreach (var digest in earlier)
            {
                prompt.AppendLine($"#{digest.Step.Sequence}: {Clip(digest.Step.Headline, MaxHeadline)}");
            }

            prompt.AppendLine("</earlier-steps>");
        }

        prompt.AppendLine("<steps>");
        foreach (var digest in chunk)
        {
            prompt.Append(digest.Text);
        }

        return prompt.AppendLine("</steps>")
            .AppendLine($"Brief steps {string.Join(", ", wanted.Order())}.")
            .ToString();
    }

    private static string OverviewPrompt(
        string header, IReadOnlyList<StepDigest> digests, IReadOnlyList<StepBrief> briefs, IReadOnlyList<ChatMessage?> messages)
    {
        var bySequence = briefs.ToDictionary(b => b.Sequence, b => b.Text);
        var steps = digests.ToDictionary(d => d.Step.Sequence);
        var prompt = new StringBuilder().AppendLine("<run>").Append(header).AppendLine("</run>").AppendLine("<timeline>");
        for (var sequence = 1; sequence < messages.Count; sequence++)
        {
            if (steps.TryGetValue(sequence, out var digest))
            {
                prompt.AppendLine($"#{sequence}: {Clip(digest.Step.Headline, MaxHeadline)}");
                if (bySequence.TryGetValue(sequence, out var brief))
                {
                    prompt.AppendLine($"    {brief}");
                }
            }
            else if (messages[sequence] is { Role: ChatRole.User, Text: { Length: > 0 } nudge })
            {
                prompt.AppendLine($"#{sequence} engine: {Clip(nudge, MaxHeadline)}");
            }
        }

        prompt.AppendLine("</timeline>");
        if (messages.LastOrDefault(m => m is { Role: ChatRole.Assistant, ToolCalls.Count: 0 }) is { Text: { Length: > 0 } last })
        {
            prompt.AppendLine("<final-message>").AppendLine(Clip(last, 2_000)).AppendLine("</final-message>");
        }

        return prompt.ToString();
    }

    private string Header(BriefCandidate run, RunSummary summary, IReadOnlyList<ChatMessage?> messages)
    {
        var header = new StringBuilder()
            .AppendLine($"Run {run.RunId}, document family {run.Family}, {run.Mode} mode, {run.Status}.");
        if (run.Failure is { } failure)
        {
            header.AppendLine($"Failure: {failure}");
        }

        if (messages.FirstOrDefault() is { Text: { Length: > 0 } task })
        {
            header.AppendLine($"Task: {Clip(task, options.MaxTextChars)}");
        }

        header.AppendLine($"Conversation: {messages.Count} messages, {messages.Count(m => m?.Role == ChatRole.Assistant)} model turns.");
        if (summary.Stages.Count > 0)
        {
            header.AppendLine("Stage attempts:");
            foreach (var record in summary.Stages)
            {
                var failed = record.Verdicts.Where(v => v.Verdict == Verdict.Fail).Select(v => v.VerifierId).ToList();
                header.AppendLine(
                    $"- {record.StageId} by {record.Executor} ({record.Tier}){(record.IsShadow ? ", shadow" : "")}: "
                    + (failed.Count == 0 ? "passed" : $"failed {string.Join(", ", failed)}")
                    + $", ${record.Result.Cost.Amount:0.####}");
            }
        }

        if (summary.Decisions.Count > 0)
        {
            header.AppendLine("Decisions:");
            foreach (var decision in summary.Decisions)
            {
                header.AppendLine($"- {decision.Key}: {Clip(decision.Choice, 200)} ({Clip(decision.Rationale, 300)})");
            }
        }

        return header.ToString();
    }

    private static SkillRef? Published(StepCall call, ToolResult? result)
    {
        if (call.Effects.LastOrDefault(e => e.Kind == StepEffectKind.SkillPublished)?.Subject is { } subject
            && subject.LastIndexOf('@') is var at and > 0
            && int.TryParse(subject[(at + 1)..], out var version))
        {
            return new SkillRef(subject[..at], version);
        }

        // A step recorded after the fact has no effects; create-skill returns the ref, create-code nests it.
        try
        {
            using var json = JsonDocument.Parse(result?.Content ?? "");
            var root = json.RootElement;
            var reference = root.TryGetProperty("skill", out var nested) ? nested : root;
            return reference.TryGetProperty("id", out var id) && reference.TryGetProperty("version", out var v) && v.TryGetInt32(out var n)
                ? new SkillRef(id.GetString() ?? "", n)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The script's own arguments, without the instructions or source a change diff shows instead.
    private static string Args(JsonElement input)
    {
        if (!input.TryGetProperty("args", out var args) || JsonNode.Parse(args.GetRawText()) is not JsonObject node)
        {
            return "{}";
        }

        foreach (var name in BulkyArgs)
        {
            if (node[name] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                node[name] = $"({text.Length:N0} characters)";
            }
        }

        return node.ToJsonString();
    }

    private static IReadOnlyList<ToolResult> ResultsOf(IReadOnlyList<ChatMessage?> messages, int sequence) =>
        sequence + 1 < messages.Count ? messages[sequence + 1]?.ToolResults ?? [] : [];

    private static ChatMessage? Read(string json)
    {
        try
        {
            return TranscriptJson.Deserialize(json).Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Clip(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length <= max ? line : $"{line[..max]}… [{line.Length:N0} characters]";
    }

    private static string ClipBlock(string text, int max) =>
        text.Length <= max ? text.TrimEnd() : $"{text[..max].TrimEnd()}\n… [{text.Length - max:N0} more characters]";

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(line => "    " + line));
}
