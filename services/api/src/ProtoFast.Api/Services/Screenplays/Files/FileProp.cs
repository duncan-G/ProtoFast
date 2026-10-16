namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileProp
{
    /// <summary>Unique among the file's props; only rows in the same file refer to it.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
}
