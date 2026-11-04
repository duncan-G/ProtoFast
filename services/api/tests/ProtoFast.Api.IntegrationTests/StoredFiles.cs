using System.Collections.Concurrent;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>The bucket as the documents service sees it: which keys hold a file, and links that name them.</summary>
public sealed class StoredFiles : IObjectStore, IPresignedUrlFactory
{
    private readonly ConcurrentDictionary<string, byte> _keys = new();

    public void Put(string key) => _keys[key] = 0;

    public bool Holds(string key) => _keys.ContainsKey(key);

    public IReadOnlyList<string> Keys(string userId) =>
        _keys.Keys.Where(k => k.StartsWith($"offtheloop/{userId}/", StringComparison.Ordinal)).ToList();

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(Holds(key));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public string PresignGet(string key, TimeSpan? ttl = null, string? fileName = null) =>
        $"https://files.test/{key}?name={Uri.EscapeDataString(fileName ?? "")}";

    public PresignedPostPolicy PresignPost(string key, string contentType, long maxBytes, TimeSpan? ttl = null) =>
        new("https://files.test/", new Dictionary<string, string> { ["key"] = key }, DateTimeOffset.UtcNow.AddMinutes(15), maxBytes);

    public string PresignPut(string key, string contentType, TimeSpan? ttl = null) => throw new NotSupportedException();

    public Task<ObjectRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<T?> ReadAsync<T>(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<string?> ReadTextAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ObjectRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ObjectRef> WriteTextAsync(
        string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ObjectRef> WriteJsonLinesAsync<T>(
        string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ObjectRef> WriteFrozenAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ObjectRef> WriteFrozenBytesAsync(
        string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
