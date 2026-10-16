namespace ProtoFast.Api.Services.Screenplays.Files;

/// <summary>Each library entry's id in the file, by its row id.</summary>
public sealed record FileKeys(
    IReadOnlyDictionary<Guid, string> Characters,
    IReadOnlyDictionary<Guid, string> Locations,
    IReadOnlyDictionary<Guid, string> Props);
