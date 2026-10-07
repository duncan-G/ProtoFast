using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Screenplay.Classification;

public sealed record NewFamily(
    string? Name,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    string? Description);
