using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Storage;

/// <summary>A pointer to one phase artifact. Messages carry these, never payloads (plan §13.2).</summary>
public sealed record ArtifactRef(string RunId, string Key, string Hash, long SizeBytes)
{
    public static readonly ArtifactRef None = new(string.Empty, string.Empty, string.Empty, 0);

    public bool IsEmpty => Key.Length == 0;
}

/// <summary>
/// Every phase leaves a file (plan §1). This is the interface that promise is kept through:
/// artifacts are written atomically, stamped with the idempotency key that produced them, and
/// checked for that key before a phase does any work.
/// </summary>
public interface IArtifactStore
{
    Task<ArtifactRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default);

    Task<ArtifactRef> WriteJsonLinesAsync<T>(
        string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default);

    Task<T?> ReadAsync<T>(string key, CancellationToken ct = default);

    Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default);

    Task<string?> ReadTextAsync(string key, CancellationToken ct = default);

    Task<ArtifactRef> WriteTextAsync(
        string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default);

    /// <summary>
    /// True when this key already holds output from the same idempotency key. This single call is
    /// what makes a re-delivered SQS message free: the phase returns the existing artifact instead
    /// of re-running (plan N4).
    /// </summary>
    Task<ArtifactRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Writes the frozen artifact with an object-lock retention, which is what makes "frozen" a
    /// storage-level fact rather than a convention (plan §9.11).
    /// </summary>
    Task<ArtifactRef> WriteFrozenAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);

    Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default);
}

/// <summary>
/// Mints the presigned URLs the browser uses. Separate from <see cref="IArtifactStore"/> because
/// <c>api</c> needs only this half: it presigns, enqueues and reads Postgres, and never touches an
/// artifact's contents (plan §17).
/// </summary>
public interface IPresignedUrlFactory
{
    string PresignPut(string key, string contentType, TimeSpan? ttl = null);

    string PresignGet(string key, TimeSpan? ttl = null);
}

/// <summary>The SQS message body for a run. It carries a run id and nothing else (plan §24.1).</summary>
public sealed record RunMessage(string RunId, RunPriority Priority)
{
    /// <summary>
    /// W3C trace context, restored by the consumer. Without it the <c>api</c> half of a submission
    /// and the worker half show up as two unrelated traces (plan §25.1).
    /// </summary>
    public string? TraceParent { get; init; }

    public string? TraceState { get; init; }

    /// <summary>Set by <c>RerunFrom</c>: resume from this phase, invalidating everything after it.</summary>
    public int? FromPhase { get; init; }
}

/// <summary>A delayed poll for a provider batch (plan §14.8).</summary>
public sealed record BatchPollMessage(string RunId, string BatchId, string Provider, AgentRole Role, int Attempt)
{
    public string? TraceParent { get; init; }
}
