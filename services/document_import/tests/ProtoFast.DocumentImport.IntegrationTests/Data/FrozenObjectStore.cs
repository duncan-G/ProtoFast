using System.Collections.Concurrent;
using System.Security.Cryptography;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.IntegrationTests.Data;

/// <summary>Only the frozen-object calls the registry makes; a second write to a key fails, as object lock would.</summary>
internal sealed class FrozenObjectStore : IObjectStore
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public int Writes;

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(_objects.ContainsKey(key));

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(_objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task<ObjectRef> WriteFrozenBytesAsync(
        string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default)
    {
        if (!_objects.TryAdd(key, content))
        {
            throw new InvalidOperationException($"{key} is frozen.");
        }

        Interlocked.Increment(ref Writes);
        return Task.FromResult(new ObjectRef(key, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length));
    }

    public Task<ObjectRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<T?> ReadAsync<T>(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string?> ReadTextAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ObjectRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ObjectRef> WriteTextAsync(string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ObjectRef> WriteJsonLinesAsync<T>(string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ObjectRef> WriteFrozenAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
}
