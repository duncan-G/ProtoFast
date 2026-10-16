namespace ProtoFast.Api.Services.Screenplays.Files;

public sealed class FileVocabulary
{
    public List<string> TimesOfDay { get; init; } = [];

    public List<string> Transitions { get; init; } = [];

    public List<string> Extensions { get; init; } = [];

    public List<FileCharacterKind> CharacterKinds { get; init; } = [];
}
