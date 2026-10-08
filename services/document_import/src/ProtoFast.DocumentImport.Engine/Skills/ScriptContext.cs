using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>Everything an agent-written script can reach: its arguments, artifacts, and other skills' scripts.</summary>
public sealed class ScriptContext
{
    private readonly SkillRuntime _runtime;
    private readonly int _depth;

    internal ScriptContext(SkillRuntime runtime, JsonElement args, int depth, CancellationToken cancellation)
    {
        _runtime = runtime;
        _depth = depth;
        Args = args;
        Cancellation = cancellation;
    }

    public JsonElement Args { get; }

    public CancellationToken Cancellation { get; }

    public T Arg<T>(string name) => SkillArgs.Required<T>(Args, name);

    public Task<string> ReadTextAsync(ArtifactRef artifact) => _runtime.ReadTextAsync(artifact);

    /// <summary>Throws when the script fails, so a caller cannot carry on past it.</summary>
    public async Task<JsonElement> RunAsync(string skill, string script, object? args = null)
    {
        var result = await _runtime.RunAsync(skill, script, SkillJson.ToElement(args ?? new { }), _depth + 1);
        if (result.IsError)
        {
            throw new InvalidOperationException($"{skill}/{script} failed: {result.Content}");
        }

        using var json = JsonDocument.Parse(result.Content);
        return json.RootElement.Clone();
    }
}
