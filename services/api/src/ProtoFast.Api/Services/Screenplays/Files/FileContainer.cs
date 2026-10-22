namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileContainer
{
    public required string Label { get; init; }

    public List<FileScene> Scenes { get; init; } = [];
}
