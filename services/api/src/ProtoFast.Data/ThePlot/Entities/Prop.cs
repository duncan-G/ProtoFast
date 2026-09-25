using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>An object that matters to the plot. Part of the story library; beats reference it with <c>@</c>.</summary>
public sealed class Prop : IDateStamped
{
    public Guid Id { get; set; }

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    /// <inheritdoc cref="CastMember.Name"/>
    public required string Name { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
