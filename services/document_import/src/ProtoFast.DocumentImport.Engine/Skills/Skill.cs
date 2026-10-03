namespace ProtoFast.DocumentImport.Engine.Skills;

/// <summary>An agent-written skill: instructions it loads on demand, and the scripts it runs with them.</summary>
public sealed record Skill(SkillRef Ref, string Description, string Instructions, IReadOnlyList<SkillScript> Scripts);
