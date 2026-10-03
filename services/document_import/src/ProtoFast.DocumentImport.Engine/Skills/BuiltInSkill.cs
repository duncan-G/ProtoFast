using System.Text.Json;

namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>An engine primitive wrapped as a skill: its instructions ship with the engine, its one script is native.</summary>
internal sealed record BuiltInSkill(
    string Id, string Description, string Instructions, string Script, Func<SkillRuntime, JsonElement, Task<object?>> Run);
