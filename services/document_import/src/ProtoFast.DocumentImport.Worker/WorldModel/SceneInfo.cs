using ProtoFast.Data.ThePlot.WorldModel;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <param name="LocationId">The first heading's location; null when the scene has none.</param>
public sealed record SceneInfo(
    Guid Id,
    string Title,
    int Container,
    int Position,
    Guid? LocationId,
    string? TimeOfDay,
    IReadOnlyList<BeatInfo> Beats)
{
    public SceneSlot Slot => new(Id, LocationId, Container, Position);
}
