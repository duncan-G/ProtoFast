namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileScene
{
    public required string Title { get; init; }

    public List<FileElement> Elements { get; init; } = [];
}
