namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileCharacter
{
    /// <summary>Unique among the file's characters; only rows in the same file refer to it.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required int Hue { get; init; }
}
