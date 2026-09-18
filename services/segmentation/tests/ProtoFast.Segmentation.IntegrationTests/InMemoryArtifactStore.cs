using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// An in-memory <see cref="IArtifactStore"/> with the same contract as the S3 one — including the
/// idempotency metadata, which is the part the pipeline's "already done?" gate depends on.
///
/// <para>Testing against this rather than a LocalStack container is deliberate: what these tests
/// are about is the phase logic and the idempotency rules, and a container would make them slow
/// enough that people stop running them. The S3 implementation's own behaviour — presigning,
/// object lock, CORS — is exercised by <c>aspire run</c> against LocalStack.</para>
/// </summary>
public sealed class InMemoryArtifactStore : IArtifactStore, IPresignedUrlFactory
{
    private sealed record Entry(byte[] Content, string IdempotencyKey, string Hash, bool Locked);

    private readonly ConcurrentDictionary<string, Entry> _objects = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyCollection<string> Keys => _objects.Keys.ToList();

    /// <summary>How many times a key has been written. The test for "a re-run is free" reads this.</summary>
    public ConcurrentDictionary<string, int> WriteCounts { get; } = new(StringComparer.Ordinal);

    public bool IsLocked(string key) => _objects.TryGetValue(key, out var entry) && entry.Locked;

    public Task<ArtifactRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        Put(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), idempotencyKey, locked: false);

    public Task<ArtifactRef> WriteJsonLinesAsync<T>(
        string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(JsonSerializer.Serialize(value, Json)).Append('\n');
        }

        return Put(key, Encoding.UTF8.GetBytes(builder.ToString()), idempotencyKey, locked: false);
    }

    public Task<ArtifactRef> WriteTextAsync(
        string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default) =>
        Put(key, Encoding.UTF8.GetBytes(content), idempotencyKey, locked: false);

    public Task<ArtifactRef> WriteFrozenAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        Put(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), idempotencyKey, locked: true);

    public Task<T?> ReadAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var entry)
            ? JsonSerializer.Deserialize<T>(entry.Content, Json)
            : default);

    public Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_objects.TryGetValue(key, out var entry))
        {
            return Task.FromResult<IReadOnlyList<T>>([]);
        }

        var values = Encoding.UTF8.GetString(entry.Content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<T>(line, Json))
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();

        return Task.FromResult<IReadOnlyList<T>>(values);
    }

    public Task<string?> ReadTextAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var entry) ? Encoding.UTF8.GetString(entry.Content) : null);

    public Task<ArtifactRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(
            _objects.TryGetValue(key, out var entry)
                && string.Equals(entry.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
                ? new ArtifactRef(string.Empty, key, entry.Hash, entry.Content.Length)
                : null);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_objects.ContainsKey(key));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        // Locked objects reject deletion, exactly as S3 does with a GOVERNANCE retention and a
        // role that has no BypassGovernanceRetention.
        if (_objects.TryGetValue(key, out var entry) && !entry.Locked)
        {
            _objects.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. _objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order()]);

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        if (_objects.TryGetValue(sourceKey, out var entry))
        {
            _objects[destinationKey] = entry with { Locked = false };
        }

        return Task.CompletedTask;
    }

    public string PresignPut(string key, string contentType, TimeSpan? ttl = null) =>
        $"https://s3.test/{key}?method=PUT";

    public string PresignGet(string key, TimeSpan? ttl = null) =>
        $"https://s3.test/{key}?method=GET";

    /// <summary>
    /// The same field set the S3 signer returns, with a stand-in signature. The tests assert the
    /// shape — that the key and the type are pinned and that the cap is echoed — because that is
    /// what the browser replays; the signature itself is AWS's to validate.
    /// </summary>
    public PresignedPost PresignPost(string key, string contentType, long maxBytes, TimeSpan? ttl = null)
    {
        var expires = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(15));

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["Content-Type"] = contentType,
            ["success_action_status"] = "201",
            ["x-amz-algorithm"] = "AWS4-HMAC-SHA256",
            ["x-amz-credential"] = "test/20260101/us-west-2/s3/aws4_request",
            ["x-amz-date"] = "20260101T000000Z",
            ["policy"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $$"""{"conditions":[{"key":"{{key}}"},["content-length-range",1,{{maxBytes}}]]}""")),
            ["x-amz-signature"] = "test-signature",
        };

        return new PresignedPost("https://s3.test/test-bucket", fields, expires, maxBytes);
    }

    private Task<ArtifactRef> Put(string key, byte[] content, string idempotencyKey, bool locked)
    {
        var hash = Ids.Sha256Hex(content);
        _objects[key] = new Entry(content, idempotencyKey, hash, locked);
        WriteCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
        return Task.FromResult(new ArtifactRef(string.Empty, key, hash, content.Length));
    }
}
