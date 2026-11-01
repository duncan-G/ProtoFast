using System.Collections.Concurrent;
using System.Security.Cryptography;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Only the plain byte calls media makes.</summary>
public sealed class MemoryObjectStore : IObjectStore
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    public Task<ObjectRef> WriteBytesAsync(
        string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default)
    {
        Objects[key] = content;
        return Task.FromResult(new ObjectRef(key, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length));
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(Objects.ContainsKey(key));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
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
    public Task<ObjectRef> WriteFrozenBytesAsync(string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) => throw new NotSupportedException();
}
