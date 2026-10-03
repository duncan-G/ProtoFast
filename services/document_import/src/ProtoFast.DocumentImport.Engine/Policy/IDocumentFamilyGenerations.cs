namespace ProtoFast.DocumentImport.Engine.Policy;

/// <summary>
/// Resetting a family starts a new generation, and each generation is its own family to every
/// other store: its runs see none of the skills, executors, verifiers, runs, stage policies or
/// workflows of earlier generations, so discovery starts over. Earlier generations are kept.
/// </summary>
public interface IDocumentFamilyGenerations
{
    // 0 for a family that was never reset.
    Task<int> CurrentAsync(string family, CancellationToken ct);

    // Returns the new generation.
    Task<int> ResetAsync(string family, CancellationToken ct);
}
