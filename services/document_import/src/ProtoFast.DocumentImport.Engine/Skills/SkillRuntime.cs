using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>
/// One agent loop's view of its skills: the built-ins and the document family's own. Loading a
/// skill returns its instructions; running a script returns its JSON result.
/// </summary>
public sealed class SkillRuntime
{
    private const int MaxDepth = 4;
    private const int MaxSummaryText = 200;

    private readonly IReadOnlyList<IScriptSafetyReviewer> _reviewers;
    private readonly EngineOptions _options;
    private readonly CancellationToken _ct;
    private readonly Lock _spendLock = new();
    private readonly ConcurrentDictionary<string, bool> _stages = new(StringComparer.Ordinal);
    private readonly Lock _effectsLock = new();
    private readonly List<StepEffect> _effects = [];
    private readonly AsyncLocal<int> _depth = new();
    private IReadOnlyList<Skill>? _skills;
    private decimal _spentAmount;
    private TimeSpan _spentDuration;

    internal SkillRuntime(
        IAgentTools tools, ScriptCompiler compiler, IReadOnlyList<IScriptSafetyReviewer> reviewers, EngineOptions options,
        CancellationToken ct)
    {
        Tools = tools;
        Compiler = compiler;
        _reviewers = reviewers;
        _options = options;
        _ct = ct;
    }

    internal IAgentTools Tools { get; }

    internal ScriptCompiler Compiler { get; }

    public async Task<IReadOnlyList<SkillSummary>> ListAsync() =>
        BuiltInSkills.All.Values.Select(s => new SkillSummary(s.Id, s.Description, BuiltIn: true))
            .Concat((await SkillsAsync()).Select(s => new SkillSummary(s.Ref.Id, s.Description, BuiltIn: false)))
            .ToList();

    public async Task<SkillResult> LoadAsync(string skill)
    {
        if (BuiltInSkills.All.TryGetValue(skill, out var builtIn))
        {
            Note(StepEffectKind.SkillLoaded, $"Loaded built-in {skill}");
            return SkillResult.Ok(Render(skill, "built-in", builtIn.Description, builtIn.Instructions, [(builtIn.Script, builtIn.Description, null)]));
        }

        if (await FindSkillAsync(skill) is not { } own)
        {
            return Failed($"There is no skill '{skill}'.");
        }

        Note(StepEffectKind.SkillLoaded, $"Loaded {own.Ref} ({own.Scripts.Count} scripts)", own.Ref.ToString());
        var scripts = new List<(string, string, string?)>();
        foreach (var script in own.Scripts)
        {
            await using var code = await Tools.ReadCode(script.CodeHash);
            using var reader = new StreamReader(code);
            scripts.Add((script.Name, script.Description, await reader.ReadToEndAsync(_ct)));
        }

        return SkillResult.Ok(Render(skill, $"version {own.Ref.Version}", own.Description, own.Instructions, scripts));
    }

    public Task<SkillResult> RunAsync(string skill, string script, JsonElement args) => RunAsync(skill, script, args, depth: 0);

    public static bool IsBuiltIn(string skill) => BuiltInSkills.All.ContainsKey(skill);

    /// <summary>What the calls since the last take did, a script's own calls included, in the order they happened.</summary>
    public IReadOnlyList<StepEffect> TakeEffects()
    {
        lock (_effectsLock)
        {
            var effects = _effects.ToList();
            _effects.Clear();
            return effects;
        }
    }

    /// <summary>Whether the stage's latest write or delegation, from the agent or a script, passed its verifiers.</summary>
    public bool HasPassed(string stageId) => _stages.TryGetValue(stageId, out var passed) && passed;

    /// <summary>The loop's own model spend; the next write carries it, so its stage is costed.</summary>
    public void Spend(Cost cost)
    {
        lock (_spendLock)
        {
            _spentAmount += cost.Amount;
            _spentDuration += cost.Duration;
        }
    }

    internal async Task<SkillResult> RunAsync(string skill, string script, JsonElement args, int depth)
    {
        // Flows into the script and the built-ins it calls, and reverts when this call returns.
        _depth.Value = depth;
        if (depth > MaxDepth)
        {
            return Failed($"Scripts may nest at most {MaxDepth} deep.");
        }

        if (BuiltInSkills.All.TryGetValue(skill, out var builtIn))
        {
            if (script != builtIn.Script)
            {
                return Failed($"The skill '{skill}' has one script, '{builtIn.Script}'.");
            }

            try
            {
                return SkillResult.Ok(SkillJson.Serialize(await builtIn.Run(this, args)));
            }
            catch (Exception e) when (e is ArgumentException or KeyNotFoundException or JsonException or InvalidOperationException)
            {
                Note(StepEffectKind.Failed, $"{skill} failed: {Clip(e.Message)}");
                return SkillResult.Error(e.Message);
            }
        }

        if (await FindSkillAsync(skill) is not { } own)
        {
            return Failed($"There is no skill '{skill}'.");
        }

        if (own.Scripts.FirstOrDefault(s => s.Name == script) is not { } found)
        {
            return Failed($"The skill '{skill}' has no script '{script}'; it has {string.Join(", ", own.Scripts.Select(s => $"'{s.Name}'"))}.");
        }

        int at;
        lock (_effectsLock)
        {
            at = _effects.Count;
        }

        var result = await RunScriptAsync(skill, script, found, args, depth);
        var ran = $"Ran {own.Ref}/{script}" + (result.IsError ? $": {Clip(result.Content)}" : "");
        lock (_effectsLock)
        {
            // Ahead of what the script itself did, which was noted while it ran.
            _effects.Insert(Math.Min(at, _effects.Count), new StepEffect(StepEffectKind.ScriptRan, ran, depth, own.Ref.ToString()));
        }

        return result;
    }

    private async Task<SkillResult> RunScriptAsync(string skill, string script, SkillScript found, JsonElement args, int depth)
    {
        var compiled = await Compiler.LoadAsync(found.CodeHash, _ct);
        var context = new ScriptContext(this, args, depth, _ct);
        try
        {
            // On the pool, so a script that never yields cannot hold the loop past its timeout.
            var result = await Task.Run(() => compiled.InvokeAsync(context), _ct).WaitAsync(_options.ScriptTimeout, _ct);
            return SkillResult.Ok(SkillJson.Serialize(result));
        }
        catch (TimeoutException)
        {
            return SkillResult.Error($"{skill}/{script} timed out after {_options.ScriptTimeout.TotalSeconds:0}s.");
        }
        catch (Exception e) when (e is not OperationCanceledException || !_ct.IsCancellationRequested)
        {
            return SkillResult.Error($"{skill}/{script} threw {e.GetType().Name}: {e.Message}");
        }
    }

    internal async Task<string> ReadTextAsync(ArtifactRef artifact)
    {
        await using var stream = await Tools.ReadArtifact(artifact);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(_ct);
    }

    internal async Task<Skill?> FindSkillAsync(string id) => (await SkillsAsync()).FirstOrDefault(s => s.Ref.Id == id);

    internal async Task<SkillRef> DefineSkillAsync(Skill skill)
    {
        var published = await Tools.DefineSkill(skill);
        _skills = null;
        return published;
    }

    internal async Task RemoveSkillAsync(string id, string reason)
    {
        await Tools.RemoveSkill(id, reason);
        _skills = null;
    }

    /// <summary>Throws the refusal the agent reads; a reviewer that faults refuses too.</summary>
    internal async Task EnsureSafeAsync(ScriptSafetyReview review)
    {
        foreach (var reviewer in _reviewers)
        {
            ScriptSafetyVerdict verdict;
            try
            {
                verdict = await reviewer.ReviewAsync(review, _ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                throw new ArgumentException($"Script '{review.Script}' was refused: it could not be checked. Try again later.");
            }

            Spend(verdict.Cost);
            if (!verdict.Safe)
            {
                // No parameter name: it would trail the findings the agent reads.
                throw new ArgumentException(string.Join('\n', verdict.Findings.Select(f => $"- {f.Path}: {f.Message}")
                    .Prepend($"Script '{review.Script}' was refused: {verdict.Reason}")));
            }
        }
    }

    /// <summary>Notes a stage's latest outcome; a resumed loop replays the run's records through it.</summary>
    public void Recorded(string stageId, bool passed) => _stages[stageId] = passed;

    internal Cost? DrainSpend()
    {
        lock (_spendLock)
        {
            if (_spentAmount == 0 && _spentDuration == TimeSpan.Zero)
            {
                return null;
            }

            var spent = new Cost(_spentAmount, _spentDuration);
            _spentAmount = 0;
            _spentDuration = TimeSpan.Zero;
            return spent;
        }
    }

    internal void Note(StepEffectKind kind, string summary, string? subject = null)
    {
        lock (_effectsLock)
        {
            _effects.Add(new StepEffect(kind, summary, _depth.Value, subject));
        }
    }

    internal static string Clip(string text, int max = MaxSummaryText)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : line[..max] + "…";
    }

    private SkillResult Failed(string message)
    {
        Note(StepEffectKind.Failed, Clip(message));
        return SkillResult.Error(message);
    }

    private async Task<IReadOnlyList<Skill>> SkillsAsync() => _skills ??= await Tools.Skills();

    private static string Render(
        string id, string origin, string description, string instructions, IReadOnlyList<(string Name, string Description, string? Source)> scripts)
    {
        var text = new StringBuilder()
            .AppendLine($"# {id} ({origin})")
            .AppendLine(description)
            .AppendLine()
            .AppendLine(instructions)
            .AppendLine()
            .AppendLine("## Scripts");
        foreach (var (name, scriptDescription, source) in scripts)
        {
            text.AppendLine($"- `{name}`: {scriptDescription}");
            if (source is not null)
            {
                text.AppendLine("```csharp").AppendLine(source.TrimEnd()).AppendLine("```");
            }
        }

        return text.AppendLine().Append($"Run a script with execute_code(skill: \"{id}\", script: \"<name>\", args: {{...}}).").ToString();
    }
}
