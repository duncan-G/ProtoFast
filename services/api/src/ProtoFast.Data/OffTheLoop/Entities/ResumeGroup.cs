namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>A named list: a skill group ("languages"), or "open source", "talks". Owned by its <see cref="Resume"/>.</summary>
public sealed class ResumeGroup
{
    public required string Name { get; set; }

    public List<string> Items { get; set; } = [];
}
