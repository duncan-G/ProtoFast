namespace ProtoFast.DocumentImport.Engine.Families;

/// <summary>
/// What is written about each family. The classifier sorts a document by these descriptions and
/// registers a family of its own when none fits; an operator sharpens them from the console.
/// </summary>
public interface IDocumentFamilyRegistry
{
    // Alphabetical by family.
    Task<IReadOnlyList<DocumentFamilyInfo>> ListAsync(CancellationToken ct);

    // Throws InvalidOperationException when the family is already registered.
    Task CreateAsync(DocumentFamilyInfo info, CancellationToken ct);

    // Also registers a family only the ledger knew, keeping the given CreatedAt/CreatedBy.
    Task UpdateAsync(DocumentFamilyInfo info, CancellationToken ct);
}
