using SceneElementTypeRecord = ProtoFast.Data.ThePlot.Entities.SceneElementType;

namespace ProtoFast.Api.Services.Screenplays.Files;

/// <summary>A scene row; <see cref="Type"/> decides which fields it may set, as in the editor.</summary>
public sealed class FileElement
{
    public required SceneElementTypeRecord Type { get; init; }

    public string? Text { get; init; }

    /// <summary>A <see cref="FileLocation.Id"/>.</summary>
    public string? Location { get; init; }

    public string? TimeOfDay { get; init; }

    /// <summary>A <see cref="FileCharacter.Id"/>.</summary>
    public string? Speaker { get; init; }

    public string? Extension { get; init; }

    public string? Parenthetical { get; init; }

    public string? Transition { get; init; }

    public List<FileMention>? Mentions { get; init; }
}
