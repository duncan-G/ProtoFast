using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Story : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public required string Title { get; set; }

    /// <summary>The imported document the story was adapted from, if any.</summary>
    public string? SourceDocumentId { get; set; }

    public List<Container> Containers { get; set; } = [];

    public List<Character> Characters { get; set; } = [];

    public List<Location> Locations { get; set; } = [];

    public List<Prop> Props { get; set; } = [];

    public List<TimeOfDay> TimesOfDay { get; set; } = [];

    public List<Transition> Transitions { get; set; } = [];

    public List<CharacterKind> CharacterKinds { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
