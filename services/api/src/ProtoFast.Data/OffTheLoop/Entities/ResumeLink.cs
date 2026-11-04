namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>Owned by its <see cref="Resume"/> and stored in it.</summary>
public sealed class ResumeLink
{
    public string Label { get; set; } = "";

    public required string Url { get; set; }
}
