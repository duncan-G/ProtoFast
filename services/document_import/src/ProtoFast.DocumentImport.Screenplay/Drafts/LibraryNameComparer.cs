namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <summary>
/// Matches character, location and prop names the way a reader would: any case, surrounding space
/// ignored, and typographic quotes equal to straight ones, since a manuscript can spell the same
/// name both ways.
/// </summary>
public sealed class LibraryNameComparer : IEqualityComparer<string>
{
    public static readonly LibraryNameComparer Instance = new();

    private LibraryNameComparer()
    {
    }

    public bool Equals(string? x, string? y) =>
        x is null || y is null ? x == y : StringComparer.OrdinalIgnoreCase.Equals(Fold(x), Fold(y));

    public int GetHashCode(string name) => StringComparer.OrdinalIgnoreCase.GetHashCode(Fold(name));

    private static string Fold(string name) =>
        string.Create(name.AsSpan().Trim().Length, name, (span, source) =>
        {
            var trimmed = source.AsSpan().Trim();
            for (var i = 0; i < trimmed.Length; i++)
            {
                span[i] = trimmed[i] switch
                {
                    '‘' or '’' or '‚' or '‛' or '′' or 'ʼ' => '\'',
                    '“' or '”' or '„' or '‟' or '″' => '"',
                    var c => c,
                };
            }
        });
}
