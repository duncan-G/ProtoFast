using System.Reflection;
using System.Text;
using System.Text.Json;
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

    private const int ReadPage = 50_000;

    public static readonly IReadOnlyDictionary<string, BuiltInSkill> All = new[]
    {
        Define("context", "context", async (rt, _) => await rt.Tools.Context()),
        Define("read-artifact", "read-artifact", ReadArtifactAsync),
        Define("write-artifact", "write-artifact", WriteArtifactAsync),
        Define("define-playbook", "define-playbook", async (rt, args) => await rt.Tools.DefinePlaybook(new Playbook(
            new PlaybookRef(Required<string>(args, "id"), 0),
            Required<string>(args, "instructions"),
            Optional<List<Example>>(args, "examples") ?? [],
            Optional<Dictionary<string, string>>(args, "rules") ?? []))),
        Define("define-executor", "define-executor", DefineExecutorAsync),
        Define("define-verifier", "define-verifier", async (rt, args) => new
        {
            id = await rt.Tools.DefineVerifier(new VerifierSpec(
                Required<string>(args, "id"), Required<string>(args, "stageId"), Required<string>(args, "rubric"))),
        }),
        Define("delegate", "delegate", DelegateAsync),
        Define("record-decision", "record-decision", async (rt, args) =>
        {
            await rt.Tools.Record(new Decision(
                Required<string>(args, "key"), Required<string>(args, "choice"), Required<string>(args, "rationale"),
                OptionalValue<double>(args, "confidence") ?? 1));
            return new { };
        }),
        Define(CreateSkill, "create", CreateSkillAsync),
        Define(CreateCode, "create", CreateCodeAsync),
    }.ToDictionary(s => s.Id, StringComparer.Ordinal);

    private static async Task<object?> ReadArtifactAsync(SkillRuntime rt, JsonElement args)
    {
        var text = await rt.ReadTextAsync(Required<ArtifactRef>(args, "artifact"));
        var offset = Math.Clamp(OptionalValue<int>(args, "offset") ?? 0, 0, text.Length);
        var length = Math.Clamp(OptionalValue<int>(args, "length") ?? ReadPage, 0, text.Length - offset);
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
        return new { artifact = written.Ref, passed, verdicts = written.Verdicts };
    }

    private static async Task<object?> DefineExecutorAsync(SkillRuntime rt, JsonElement args)
    {
        var tier = Required<Tier>(args, "tier");
        return await rt.Tools.DefineExecutor(new ExecutorSpec(
            new ExecutorRef(Required<string>(args, "id"), 0),
            tier,
            ModelClasses.For(tier),
            OptionalValue<PlaybookRef>(args, "playbook"),
            Optional<List<string>>(args, "tools") ?? [],
            Optional<string>(args, "codeAssembly"),
            ExecutorOrigin.AgentDefined,
            Promoted: false));
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
        return new { artifact = record.Output, passed = record.Passed, tier = record.Tier, verdicts = record.Verdicts };
    }

    private static async Task<object?> CreateSkillAsync(SkillRuntime rt, JsonElement args)
    {
        var id = Required<string>(args, "name");
        var existing = await rt.FindSkillAsync(id);
        return await rt.DefineSkillAsync(new Skill(
            new SkillRef(id, 0), Required<string>(args, "description"), Required<string>(args, "instructions"), existing?.Scripts ?? []));
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
        return new { skill = published, script = name, hash };
    }

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
