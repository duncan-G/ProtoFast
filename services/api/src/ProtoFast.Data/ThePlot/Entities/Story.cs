using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A story a writer is adapting into scenes: the root of the screenplay tree
/// (<see cref="Draft"/> → <see cref="Act"/> → <see cref="Scene"/> → <see cref="SceneElement"/>) and
/// the owner of the story library — the <see cref="CastMember"/>s, <see cref="Location"/>s and
/// <see cref="Prop"/>s every draft of it shares.
/// </summary>
public sealed class Story : IDateStamped
{
    public Guid Id { get; set; }

    /// <summary>
    /// The owner's subject from the internal JWT. Stamped from the caller on insert and never
    /// taken from a request; reads are filtered to it and writes for another owner are refused.
    /// </summary>
    public string UserId { get; set; } = "";

    public required string Title { get; set; }

    /// <summary>
    /// The imported <see cref="Document"/> the story was adapted from, or null for one started
    /// blank. Cleared, not cascaded, if the document is deleted: the adaptation outlives its source.
    /// </summary>
    public string? SourceDocumentId { get; set; }

    public List<Draft> Drafts { get; set; } = [];

    public List<CastMember> Cast { get; set; } = [];

    public List<Location> Locations { get; set; } = [];

    public List<Prop> Props { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
