using System.Text.Json.Serialization;

namespace ProtoFast.DocumentImport.Screenplay.Classification;

/// <summary>The classifier model's answer: a registered family, or a new one when none fits.</summary>
public sealed record FamilyChoice(
    string? Family,
    [property: JsonPropertyName("new_family")] NewFamily? NewFamily,
    string? Reason);
