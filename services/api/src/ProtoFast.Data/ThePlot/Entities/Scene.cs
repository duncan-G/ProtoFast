using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Scene : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid ContainerId { get; set; }

    public Container Container { get; set; } = null!;

    public int Position { get; set; }

    public required string Title { get; set; }

    public List<SceneElement> Elements { get; set; } = [];

    /// <summary>Which day of the story the scene happens on; looks reset between days.</summary>
    public int? StoryDay { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.Shots"/> label every frame takes unless it sets its own.</summary>
    public string? Shot { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.Angles"/> label, the default for every frame.</summary>
    public string? Angle { get; set; }

    /// <summary>A <see cref="DirectionVocabulary.Movements"/> label, the default for every frame.</summary>
    public string? Movement { get; set; }

    public List<Frame> Frames { get; set; } = [];

    public List<Cue> Cues { get; set; } = [];

    public List<SceneAppearance> Appearances { get; set; } = [];

    public List<SceneLookChange> LookChanges { get; set; } = [];

    public List<ScenePropState> PropStates { get; set; } = [];

    public List<SceneLocationState> LocationStates { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
