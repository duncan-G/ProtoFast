using ProtoFast.DocumentImport.Engine.Skills;

namespace ProtoFast.DocumentImport.Engine.Families;

// Removed skills stay listed: the agent no longer sees them, but their history does not go away.
public sealed record FamilySkill(SkillRef Ref, DateTimeOffset AddedAt, DateTimeOffset? RemovedAt = null, string? RemovalReason = null);
