using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class CastMember : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Name { get; set; }

    public CastMemberKind Kind { get; set; }

    /// <summary>Avatar colour as an OKLCH hue, 0–359.</summary>
    public int Hue { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
