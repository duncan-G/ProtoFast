namespace ProtoFast.DocumentImport.Engine;

public interface IVerifier
{
    string Id { get; }
    bool IsDeterministic { get; }
    Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct);
}
