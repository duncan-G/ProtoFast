using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <param name="LocationId">The first heading's location; null when the scene has none.</param>
/// <param name="Cast">The characters who speak or are mentioned in it.</param>
public sealed record SceneInfo(
    Guid Id,
    string Title,
    int Container,
    int Position,
    Guid? LocationId,
    string? TimeOfDay,
    IReadOnlyList<BeatInfo> Beats,
    IReadOnlySet<Guid>? Cast = null)
{
    /// <summary>It shares a location or a character with <paramref name="other"/>, so one must be read before the other.</summary>
    public bool Meets(SceneInfo other) =>
        LocationId == other.LocationId || Cast is not null && other.Cast is not null && Cast.Overlaps(other.Cast);

    public SceneSlot Slot => new(Id, LocationId, Container, Position);
}
