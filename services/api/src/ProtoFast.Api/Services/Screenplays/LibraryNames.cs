using System.Globalization;

namespace ProtoFast.Api.Services.Screenplays;

/// <summary>Ports the client's <c>library-names.ts</c>, wording included.</summary>
public static class LibraryNames
{
    public const int MaxLength = 255;

    /// <summary>Ignores case and surrounding space, as the client's <c>sameName</c> does.</summary>
    public static bool Same(string a, string b) =>
        string.Compare(a.Trim(), b.Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase) == 0;

    /// <returns>The name trimmed.</returns>
    public static string Check(IEnumerable<(Guid Id, string Name)> entries, string? name, string noun, Guid? exceptId = null)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw StoryErrors.Invalid($"A {noun} needs a name.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw StoryErrors.Invalid($"Names are limited to {MaxLength} characters.");
        }

        foreach (var entry in entries)
        {
            if (entry.Id != exceptId && Same(entry.Name, trimmed))
            {
                throw StoryErrors.AlreadyExists($"There’s already a {noun} called “{entry.Name}”.");
            }
        }

        return trimmed;
    }

    public static int Hue(int hue) =>
        hue is >= 0 and <= 359 ? hue : throw StoryErrors.Invalid("Hues run from 0 to 359.");
}
