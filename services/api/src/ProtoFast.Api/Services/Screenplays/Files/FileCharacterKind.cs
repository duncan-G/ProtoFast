using AvatarShapeRecord = ProtoFast.Data.ThePlot.Entities.AvatarShape;

namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileCharacterKind
{
    public required string Label { get; init; }

    public required AvatarShapeRecord AvatarShape { get; init; }
}
