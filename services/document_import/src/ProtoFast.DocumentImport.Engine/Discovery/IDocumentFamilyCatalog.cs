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
    Task<IReadOnlyList<SkillRef>> SkillsAsync(string family, CancellationToken ct);
}
