namespace ProtoFast.DocumentImport.Engine;

public sealed record WriteResult(ArtifactRef Ref, IReadOnlyList<VerifierResult> Verdicts);
