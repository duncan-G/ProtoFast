namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class DocumentFamilySkill
{
    public required string Family { get; set; }

    public required string SkillId { get; set; }

    public int SkillVersion { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public string? RemovalReason { get; set; }
}
