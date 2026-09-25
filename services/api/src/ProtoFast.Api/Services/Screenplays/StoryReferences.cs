namespace ProtoFast.Api.Services.Screenplays;

/// <summary>What a scene in the story may point at.</summary>
public sealed class StoryReferences
{
    public required IReadOnlySet<Guid> CharacterIds { get; init; }

    public required IReadOnlySet<Guid> LocationIds { get; init; }

    public required IReadOnlySet<Guid> PropIds { get; init; }

    /// <summary>The defaults, then the story's additions.</summary>
    public required IReadOnlyList<string> TimesOfDay { get; init; }

    /// <summary>The defaults, then the story's additions.</summary>
    public required IReadOnlyList<string> Transitions { get; init; }
}
