namespace ProtoFast.Api.Services.Screenplays.Files;

/// <summary>
/// A whole story as one <c>.theplot</c> file. Array order is position, and rows refer to library
/// entries by the file's own ids, so an import maps it straight onto new rows.
/// </summary>
public sealed class TheplotFile
{
    public const string FormatName = "theplot";

    public const int CurrentVersion = 1;

    public required string Format { get; init; }

    public required int Version { get; init; }

    public required string Title { get; init; }

    /// <summary>Only the story's additions to the default vocabulary.</summary>
    public FileVocabulary Vocabulary { get; init; } = new();

    public List<FileCharacter> Characters { get; init; } = [];

    public List<FileLocation> Locations { get; init; } = [];

    public List<FileProp> Props { get; init; } = [];

    public required List<FileContainer> Containers { get; init; }
}
