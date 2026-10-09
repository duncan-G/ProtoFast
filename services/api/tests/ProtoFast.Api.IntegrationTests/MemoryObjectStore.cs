using System.Collections.Concurrent;
using System.Security.Cryptography;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Holds written media in memory; everything else is not needed by the api under test.</summary>
public sealed class MemoryObjectStore : IObjectStore, IPresignedUrlFactory
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(Objects.ContainsKey(key));

    public Task<ObjectRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Objects.Keys.Where(k => k.StartsWith(prefix)).ToList());

    public Task<T?> ReadAsync<T>(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<string?> ReadTextAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Objects.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task<ObjectRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ObjectRef> WriteTextAsync(string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default) =>
        WriteBytesAsync(key, System.Text.Encoding.UTF8.GetBytes(content), contentType, idempotencyKey, ct);

    public Task<ObjectRef> WriteJsonLinesAsync<T>(string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ObjectRef> WriteFrozenAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ObjectRef> WriteFrozenBytesAsync(string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default) =>
        WriteBytesAsync(key, content, contentType, idempotencyKey, ct);

    public Task<ObjectRef> WriteBytesAsync(string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default)
    {
        Objects[key] = content;
        return Task.FromResult(new ObjectRef(key, Convert.ToHexStringLower(SHA256.HashData(content)), content.Length));
    }

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) => throw new NotSupportedException();

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public string PresignPut(string key, string contentType, TimeSpan? ttl = null) => $"https://media.test/{key}?put";

    public string PresignGet(string key, TimeSpan? ttl = null) => $"https://media.test/{key}";

    public PresignedPostPolicy PresignPost(string key, string contentType, long maxBytes, TimeSpan? ttl = null) => throw new NotSupportedException();
}
