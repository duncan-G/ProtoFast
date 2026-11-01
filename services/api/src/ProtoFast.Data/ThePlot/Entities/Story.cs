using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Story : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public required string Title { get; set; }

    /// <summary>The open story this one was copied from; it may since have been withdrawn.</summary>
    public Guid? SourceStoryId { get; set; }

    public List<Container> Containers { get; set; } = [];

    public List<Character> Characters { get; set; } = [];

    public List<Location> Locations { get; set; } = [];

    public List<Prop> Props { get; set; } = [];

    public StoryVocabulary Vocabulary { get; set; } = new();

    /// <summary>Where the writer left the story. Plain ids, not keys: a spot can name a block not yet saved.</summary>
    public Guid? SpotSceneId { get; set; }

    /// <summary>The block at the top of the view; null at the scene's top.</summary>
    public Guid? SpotElementId { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
