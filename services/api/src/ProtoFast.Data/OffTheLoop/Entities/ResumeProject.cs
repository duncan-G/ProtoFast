namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>Owned by its <see cref="Resume"/> and stored in it.</summary>
public sealed class ResumeProject
{
    public required string Name { get; set; }

    public string Link { get; set; } = "";

    public string Start { get; set; } = "";

    /// <summary>Empty while it's ongoing.</summary>
    public string End { get; set; } = "";

    public string Summary { get; set; } = "";

    public List<string> Highlights { get; set; } = [];

    public List<string> Stack { get; set; } = [];

    public List<ResumeEvidence> Evidence { get; set; } = [];
}
