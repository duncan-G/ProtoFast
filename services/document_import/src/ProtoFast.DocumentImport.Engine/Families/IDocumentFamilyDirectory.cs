namespace ProtoFast.DocumentImport.Engine.Families;

/// <summary>
/// Families as the console sees them: the ones the classifier or an operator registered, the ones
/// only the ledger knows, and what each has learned so far.
/// </summary>
public interface IDocumentFamilyDirectory
{
    // Alphabetical by family.
    Task<IReadOnlyList<DocumentFamilySummary>> ListAsync(CancellationToken ct);

    // Null when nothing anywhere knows the family. A null generation reads the current one.
    Task<DocumentFamilyDetail?> FindAsync(string family, int? generation, CancellationToken ct);
}
