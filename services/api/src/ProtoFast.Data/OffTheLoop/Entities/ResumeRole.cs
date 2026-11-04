namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>A job held. Owned by its <see cref="Resume"/> and stored in it.</summary>
public sealed class ResumeRole
{
    public required string Company { get; set; }

    public string Title { get; set; } = "";

    public string Location { get; set; } = "";

    public string Start { get; set; } = "";

    /// <summary>Empty while it's current.</summary>
    public string End { get; set; } = "";

    public string Summary { get; set; } = "";

    public List<string> Highlights { get; set; } = [];

    public List<string> Stack { get; set; } = [];

    public List<ResumeEvidence> Evidence { get; set; } = [];
}
