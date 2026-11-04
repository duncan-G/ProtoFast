namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>A commit a resume entry rests on. Owned by its entry and stored in it.</summary>
public sealed class ResumeEvidence
{
    public string Repo { get; set; } = "";

    public required string Commit { get; set; }

    public DateOnly? Date { get; set; }

    public string What { get; set; } = "";
}
