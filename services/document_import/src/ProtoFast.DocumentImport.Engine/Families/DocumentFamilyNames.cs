using System.Text.RegularExpressions;

namespace ProtoFast.DocumentImport.Engine.Families;

/// <summary>A family name is a storage key in every store, so it is kept to a safe shape.</summary>
public static partial class DocumentFamilyNames
{
    public const int MaxLength = 64;

    // Lowercase, starting with a letter; never '#', which marks a generation.
    public static bool IsValid(string? family) =>
        family is not null && family.Length <= MaxLength && Shape().IsMatch(family);

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex Shape();
}
