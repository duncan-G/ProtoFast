using System.Text.Json.Serialization;

namespace ProtoFast.Api.Services.Screenplays.Files;

/// <summary>
/// Points a span of its row's text at one of <see cref="Character"/>, <see cref="Location"/> or
/// <see cref="Prop"/>, by file id. Offsets and lengths count UTF-16 code units.
/// </summary>
public sealed class FileMention
{
    public required int Offset { get; init; }

    public required int Length { get; init; }

    public string? Character { get; init; }

    public string? Location { get; init; }

    public string? Prop { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Tag { get; init; }
}
