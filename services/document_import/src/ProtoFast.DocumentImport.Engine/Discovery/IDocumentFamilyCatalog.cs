using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public interface IDocumentFamilyCatalog
{
    Task AddExecutorAsync(string family, ExecutorRef executor, CancellationToken ct);
    Task<IReadOnlyList<ExecutorRef>> ExecutorsAsync(string family, CancellationToken ct);
    Task AddVerifierAsync(string family, VerifierSpec spec, CancellationToken ct);
    Task<IReadOnlyList<VerifierSpec>> VerifiersAsync(string family, CancellationToken ct);
    Task AddSkillAsync(string family, SkillRef skill, CancellationToken ct);

    // Soft: every version so far stays in the family's history, but SkillsAsync no longer lists them.
    // A later skill under the same id starts fresh. False when the family has no live skill by that id.
    Task<bool> RemoveSkillAsync(string family, string skillId, string reason, CancellationToken ct);

    /// <summary>The family's live skills, every version of each.</summary>
    Task<IReadOnlyList<SkillRef>> SkillsAsync(string family, CancellationToken ct);
}
