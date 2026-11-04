namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>Owned by its <see cref="Resume"/> and stored in it.</summary>
public sealed class ResumeSchool
{
    public required string School { get; set; }

    public string Degree { get; set; } = "";

    public string Start { get; set; } = "";

    public string End { get; set; } = "";

    public List<string> Details { get; set; } = [];
}
