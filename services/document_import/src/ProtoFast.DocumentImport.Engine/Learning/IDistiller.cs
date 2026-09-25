namespace ProtoFast.DocumentImport.Engine;

public interface IDistiller
{
    // Idempotent per (row, tier).
    Task RequestCandidateAsync(PolicyRow row, CancellationToken ct);
}
