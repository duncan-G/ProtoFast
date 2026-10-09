namespace ProtoFast.Api.Services.Screenplays;

/// <summary>The free text on a sheet: trimmed, and bounded so a model's run-on answer cannot fill a row.</summary>
public static class SheetText
{
    public const int DescriptionMaxLength = 4000;

    public const int FieldMaxLength = 1000;

    public static string? Description(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= DescriptionMaxLength
            ? trimmed
            : throw StoryErrors.Invalid($"Descriptions are limited to {DescriptionMaxLength} characters.");
    }

    public static string Field(string? text, string noun)
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Length <= FieldMaxLength
            ? trimmed
            : throw StoryErrors.Invalid($"The {noun} is limited to {FieldMaxLength} characters.");
    }

    /// <summary>Blank entries are dropped.</summary>
    public static List<string> List(IEnumerable<string> entries, string noun) =>
        entries.Select(e => Field(e, noun)).Where(e => e.Length > 0).ToList();
}
