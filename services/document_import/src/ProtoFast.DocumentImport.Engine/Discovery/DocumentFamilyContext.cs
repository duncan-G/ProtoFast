namespace ProtoFast.DocumentImport.Engine;

public sealed record DocumentFamilyContext(
    IReadOnlyList<string> StageIds,
    IReadOnlyList<ExecutorSpec> Executors,
    IReadOnlyList<VerifierSpec> Verifiers);
