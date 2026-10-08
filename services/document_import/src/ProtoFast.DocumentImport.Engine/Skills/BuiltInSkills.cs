using System.Reflection;
using System.Text;
using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using static ProtoFast.DocumentImport.Engine.Skills.SkillArgs;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>
/// The engine's primitives as skills, plus the two that let the agent grow its own. Instructions
/// live in <c>Skills/BuiltIn/{id}.md</c>, front matter first, as a skill file would.
/// </summary>
internal static class BuiltInSkills
{
    public const string CreateSkill = "create-skill";
    public const string CreateCode = "create-code";
    public const string RemoveSkill = "remove-skill";

    private const int ReadPage = 50_000;

    public static readonly IReadOnlyDictionary<string, BuiltInSkill> All = new[]
    {
        Define("context", "context", ContextAsync),
        Define("read-artifact", "read-artifact", ReadArtifactAsync),
        Define("write-artifact", "write-artifact", WriteArtifactAsync),
        Define("define-playbook", "define-playbook", async (rt, args) =>
        {
            var playbook = await rt.Tools.DefinePlaybook(new Playbook(
                new PlaybookRef(Required<string>(args, "id"), 0),
                Required<string>(args, "instructions"),
                Optional<List<Example>>(args, "examples") ?? [],
                Optional<Dictionary<string, string>>(args, "rules") ?? []));
            rt.Note(StepEffectKind.PlaybookDefined, $"Defined playbook {playbook.Id}@{playbook.Version}");
            return playbook;
        }),
        Define("define-executor", "define-executor", DefineExecutorAsync),
        Define("define-verifier", "define-verifier", async (rt, args) =>
        {
            var stageId = Required<string>(args, "stageId");
            var id = await rt.Tools.DefineVerifier(new VerifierSpec(Required<string>(args, "id"), stageId, Required<string>(args, "rubric")));
            rt.Note(StepEffectKind.VerifierDefined, $"Defined verifier {id} for stage `{stageId}`");
            return new { id };
        }),
        Define("delegate", "delegate", DelegateAsync),
        Define("record-decision", "record-decision", async (rt, args) =>
        {
            var decision = new Decision(
                Required<string>(args, "key"), Required<string>(args, "choice"), Required<string>(args, "rationale"),
                OptionalValue<double>(args, "confidence") ?? 1);
            await rt.Tools.Record(decision);
            rt.Note(StepEffectKind.DecisionRecorded, $"Decided {decision.Key}: {SkillRuntime.Clip(decision.Choice, 120)}");
            return new { };
        }),
        Define(CreateSkill, "create", CreateSkillAsync),
        Define(CreateCode, "create", CreateCodeAsync),
        Define(RemoveSkill, "remove", RemoveSkillAsync),
    }.ToDictionary(s => s.Id, StringComparer.Ordinal);

    private static async Task<object?> ContextAsync(SkillRuntime rt, JsonElement args)
    {
        var context = await rt.Tools.Context();
        rt.Note(StepEffectKind.ContextRead,
            $"Read the family context: {context.StageIds.Count} stages, {context.Executors.Count} executors, {context.Verifiers.Count} verifiers");
        return context;
    }

    private static async Task<object?> ReadArtifactAsync(SkillRuntime rt, JsonElement args)
    {
        var artifact = Required<ArtifactRef>(args, "artifact");
        var text = await rt.ReadTextAsync(artifact);
        var offset = Math.Clamp(OptionalValue<int>(args, "offset") ?? 0, 0, text.Length);
        var length = Math.Clamp(OptionalValue<int>(args, "length") ?? ReadPage, 0, text.Length - offset);
        rt.Note(StepEffectKind.ArtifactRead,
            $"Read {Label(artifact)} characters {offset:N0}–{offset + length:N0} of {text.Length:N0}", Subject(artifact));
        return new { text = text.Substring(offset, length), offset, length, totalLength = text.Length };
    }

    private static async Task<object?> WriteArtifactAsync(SkillRuntime rt, JsonElement args)
    {
        var stageId = Required<string>(args, "stageId");
        var written = await rt.Tools.WriteArtifact(
            stageId,
            new MemoryStream(Encoding.UTF8.GetBytes(Document(args, "content"))),
            Required<ContractRef>(args, "contract"),
            Optional<List<ArtifactRef>>(args, "inputs"),
            rt.DrainSpend());
        var passed = written.Verdicts.All(v => v.Verdict != Verdict.Fail);
        rt.Recorded(stageId, passed);
        rt.Note(StepEffectKind.ArtifactWritten, $"Wrote stage `{stageId}`: {Outcome(written.Verdicts)}", Subject(written.Ref));
        return new { artifact = written.Ref, passed, verdicts = written.Verdicts };
    }

    private static async Task<object?> DefineExecutorAsync(SkillRuntime rt, JsonElement args)
    {
        var tier = Required<Tier>(args, "tier");
        var executor = await rt.Tools.DefineExecutor(new ExecutorSpec(
            new ExecutorRef(Required<string>(args, "id"), 0),
            tier,
            ModelClasses.For(tier),
            OptionalValue<PlaybookRef>(args, "playbook"),
            Optional<List<string>>(args, "tools") ?? [],
            Optional<string>(args, "codeAssembly"),
            ExecutorOrigin.AgentDefined,
            Promoted: false));
        rt.Note(StepEffectKind.ExecutorDefined, $"Defined executor {executor} ({tier})");
        return executor;
    }

    private static async Task<object?> DelegateAsync(SkillRuntime rt, JsonElement args)
    {
        var stageId = Required<string>(args, "stageId");
        var record = await rt.Tools.Delegate(
            stageId,
            Required<ExecutorRef>(args, "executor"),
            Required<List<ArtifactRef>>(args, "inputs"),
            OptionalValue<ContractRef>(args, "output"));
        rt.Recorded(stageId, record.Passed);
        rt.Note(StepEffectKind.Delegated,
            $"Delegated stage `{stageId}` to {record.Executor} ({record.Tier}): {Outcome(record.Verdicts)}, ${record.Result.Cost.Amount:0.####}",
            Subject(record.Output));
        return new { artifact = record.Output, passed = record.Passed, tier = record.Tier, verdicts = record.Verdicts };
    }

    private static async Task<object?> CreateSkillAsync(SkillRuntime rt, JsonElement args)
    {
        var id = Required<string>(args, "name");
        var existing = await rt.FindSkillAsync(id);
        var instructions = Required<string>(args, "instructions");
        var scripts = existing?.Scripts ?? [];
        var remove = Optional<List<string>>(args, "remove") ?? [];
        if (remove.FirstOrDefault(name => scripts.All(s => s.Name != name)) is { } unknown)
        {
            throw new ArgumentException($"The skill '{id}' has no script '{unknown}' to remove.", nameof(args));
        }

        var published = await rt.DefineSkillAsync(new Skill(
            new SkillRef(id, 0), Required<string>(args, "description"), instructions,
            scripts.Where(s => !remove.Contains(s.Name)).ToList()));
        var removed = remove.Count > 0 ? $", removed {string.Join(", ", remove.Select(n => $"`{n}`"))}" : "";
        rt.Note(StepEffectKind.SkillPublished,
            existing is null
                ? $"Published {published} (new skill)"
                : $"Published {published}: instructions {LineDiff.Describe(existing.Instructions, instructions)}{removed}",
            published.ToString());
        return published;
    }

    private static async Task<object?> CreateCodeAsync(SkillRuntime rt, JsonElement args)
    {
        var id = Required<string>(args, "skill");
        var name = Required<string>(args, "script");
        var source = Required<string>(args, "source");
        var skill = await rt.FindSkillAsync(id)
            ?? throw new ArgumentException($"There is no skill '{id}' of yours; create it with {CreateSkill} first.", nameof(args));

        var compiled = rt.Compiler.Compile(source);
        var hash = await rt.Tools.UploadCode(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        rt.Compiler.Remember(hash, compiled);

        var script = new SkillScript(name, Required<string>(args, "description"), hash);
        var published = await rt.DefineSkillAsync(skill with
        {
            Ref = new SkillRef(id, 0),
            Scripts = skill.Scripts.Where(s => s.Name != name).Append(script).ToList(),
        });

        var previous = skill.Scripts.FirstOrDefault(s => s.Name == name);
        var change = previous is null
            ? $"new script `{name}` ({source.ReplaceLineEndings("\n").Split('\n').Length} lines)"
            : previous.CodeHash == hash
                ? $"script `{name}` unchanged"
                : $"script `{name}` {LineDiff.Describe(await ReadCodeAsync(rt, previous.CodeHash), source)}";
        rt.Note(StepEffectKind.SkillPublished, $"Published {published}: {change}", published.ToString());
        return new { skill = published, script = name, hash };
    }

    private static async Task<object?> RemoveSkillAsync(SkillRuntime rt, JsonElement args)
    {
        var id = Required<string>(args, "name");
        var reason = Required<string>(args, "reason");
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Say why the skill is removed.", nameof(args));
        }

        var skill = await rt.FindSkillAsync(id)
            ?? throw new ArgumentException($"There is no skill '{id}' of yours to remove.", nameof(args));
        await rt.RemoveSkillAsync(id, reason);
        rt.Note(StepEffectKind.SkillRemoved, $"Removed {skill.Ref}: {SkillRuntime.Clip(reason, 120)}", skill.Ref.ToString());
        return new { removed = id };
    }

    private static async Task<string> ReadCodeAsync(SkillRuntime rt, string hash)
    {
        await using var code = await rt.Tools.ReadCode(hash);
        using var reader = new StreamReader(code);
        return await reader.ReadToEndAsync();
    }

    private static string Outcome(IReadOnlyList<VerifierResult> verdicts)
    {
        var failed = verdicts.Where(v => v.Verdict == Verdict.Fail).ToList();
        if (failed.Count > 0)
        {
            return "failed " + string.Join(", ", failed.Select(v => $"{v.VerifierId} ({SkillRuntime.Clip(v.Reason, 120)})"));
        }

        var degraded = verdicts.Where(v => v.Verdict == Verdict.Degraded).Select(v => v.VerifierId).ToList();
        return degraded.Count > 0 ? $"passed, degraded on {string.Join(", ", degraded)}" : "passed";
    }

    private static string Label(ArtifactRef artifact) =>
        artifact.StageId == ArtifactRef.InputStageId ? "the input" : $"stage `{artifact.StageId}`";

    private static string Subject(ArtifactRef artifact) => $"{artifact.StageId}/{artifact.Hash}";

    private static BuiltInSkill Define(string id, string script, Func<SkillRuntime, JsonElement, Task<object?>> run)
    {
        var (description, instructions) = Read(id);
        return new BuiltInSkill(id, description, instructions, script, run);
    }

    private static (string Description, string Instructions) Read(string id)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"skills/{id}.md")
            ?? throw new InvalidOperationException($"The built-in skill '{id}' has no instructions.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().ReplaceLineEndings("\n");

        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        var description = text[4..end].Split('\n')
            .Select(line => line.Split(':', 2))
            .Single(pair => pair[0].Trim() == "description")[1].Trim();
        return (description, text[(end + 5)..].Trim());
    }
}
