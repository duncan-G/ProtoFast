namespace ProtoFast.Storage.Abstractions;

public interface IObjectStore
{
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task<ObjectRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);

    Task<T?> ReadAsync<T>(string key, CancellationToken ct = default);

    Task<string?> ReadTextAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default);

    Task<ObjectRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default);

    Task<ObjectRef> WriteTextAsync(
        string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default);

    Task<ObjectRef> WriteJsonLinesAsync<T>(
        string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Writes the frozen artifact with an object-lock retention, which is what makes "frozen" a
    /// storage-level fact rather than a convention.
    /// </summary>
    Task<ObjectRef> WriteFrozenAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default);

    Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
