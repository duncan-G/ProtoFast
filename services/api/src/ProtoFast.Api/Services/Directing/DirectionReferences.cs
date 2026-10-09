using ProtoFast.Api.Services.Screenplays;
using SceneElementRecord = ProtoFast.Data.ThePlot.Entities.SceneElement;

namespace ProtoFast.Api.Services.Directing;

/// <summary>What a scene's director layer may point at.</summary>
public sealed class DirectionReferences
{
    /// <summary>The scene's elements by id.</summary>
    public required IReadOnlyDictionary<Guid, SceneElementRecord> Elements { get; init; }

    public required StoryReferences Story { get; init; }

    /// <summary>Each character's wardrobe ids.</summary>
    public required IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> WardrobesByCharacter { get; init; }

    public required IReadOnlySet<Guid> NarratorIds { get; init; }

    public required IReadOnlySet<Guid> ExtraIds { get; init; }

    /// <summary>The defaults, then the story's additions.</summary>
    public required IReadOnlyList<string> DeliveryTags { get; init; }
}
