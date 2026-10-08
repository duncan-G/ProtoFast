namespace ProtoFast.DocumentImport.Engine.Skills;

/// <param name="CodeHash">The SHA-256 of the C# source in the registry.</param>
public sealed record SkillScript(string Name, string Description, string CodeHash);
