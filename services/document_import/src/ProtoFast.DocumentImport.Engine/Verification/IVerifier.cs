using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Engine.Verification;

public interface IVerifier
{
    string Id { get; }
    bool IsDeterministic { get; }
    Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct);
}
