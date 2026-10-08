namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>What the agent sees of a skill before it loads it.</summary>
public sealed record SkillSummary(string Id, string Description, bool BuiltIn);
