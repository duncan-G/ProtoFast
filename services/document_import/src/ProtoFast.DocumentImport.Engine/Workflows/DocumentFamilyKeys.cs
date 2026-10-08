namespace ProtoFast.DocumentImport.Engine.Workflows;

/// <summary>
/// Every family-keyed store sees a generation as its own family: generation 0 is the bare name,
/// later ones are <c>name#N</c>.
/// </summary>
public static class DocumentFamilyKeys
{
    private const char Separator = '#';

    public static string ForGeneration(string family, int generation) =>
        generation == 0 ? family : $"{family}{Separator}{generation}";

    public static (string Family, int Generation) Split(string key)
    {
        var at = key.LastIndexOf(Separator);
        return at > 0 && int.TryParse(key.AsSpan(at + 1), out var generation)
            ? (key[..at], generation)
            : (key, 0);
    }
}
