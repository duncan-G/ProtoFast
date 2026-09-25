using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public sealed record WriteResult(ArtifactRef Ref, IReadOnlyList<VerifierResult> Verdicts);
