using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.Learning;

public interface IDistiller
{
    // Idempotent per (row, tier).
    Task RequestCandidateAsync(PolicyRow row, CancellationToken ct);
}
