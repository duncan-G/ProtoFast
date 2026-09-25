using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public sealed record DocumentFamilyContext(
    IReadOnlyList<string> StageIds,
    IReadOnlyList<ExecutorSpec> Executors,
    IReadOnlyList<VerifierSpec> Verifiers);
