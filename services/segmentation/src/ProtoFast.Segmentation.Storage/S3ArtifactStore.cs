using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Storage;

/// <summary>
/// The S3 implementation of <see cref="IArtifactStore"/>.
///
/// <para>Writes stamp the idempotency key and the content hash as object metadata. That is what
/// <see cref="FindExistingAsync"/> reads, and it is deliberately metadata rather than a sidecar
/// row: the check has to be true about the object that actually exists, and a database row saying
/// a phase finished is not evidence that its artifact was written.</para>
/// </summary>
public sealed class S3ArtifactStore(
    IAmazonS3 s3,
    IOptions<StorageOptions> options,
    ILogger<S3ArtifactStore> logger) : IArtifactStore, IPresignedUrlFactory
{
    private const string IdempotencyMetadataKey = "x-amz-meta-pf-idempotency";
    private const string HashMetadataKey = "x-amz-meta-pf-hash";

    private readonly StorageOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<ArtifactRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        PutAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), idempotencyKey, "application/json", null, ct);

    public Task<ArtifactRef> WriteJsonLinesAsync<T>(
        string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(JsonSerializer.Serialize(value, Json)).Append('\n');
        }

        return PutAsync(key, Encoding.UTF8.GetBytes(builder.ToString()), idempotencyKey, "application/jsonl", null, ct);
    }

    public Task<ArtifactRef> WriteTextAsync(
        string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default) =>
        PutAsync(key, Encoding.UTF8.GetBytes(content), idempotencyKey, contentType, null, ct);

    /// <summary>
    /// GOVERNANCE retention, set at write time. The instance role deliberately has no
    /// <c>s3:BypassGovernanceRetention</c> (plan §24.1), so the worker that wrote it cannot
    /// unwrite it.
    /// </summary>
    public Task<ArtifactRef> WriteFrozenAsync<T>(
        string key, T value, string idempotencyKey, CancellationToken ct = default)
    {
        DateTime? retainUntil = _options.ObjectLockEnabled
            ? DateTime.UtcNow.AddDays(_options.FrozenLockDays)
            : null;

        if (retainUntil is null)
        {
            // LocalStack has no object lock. Say so: "frozen" is weaker here than in production,
            // and a developer reading a log should know which guarantee they are testing against.
            logger.LogInformation(
                "Object lock disabled; writing {Key} without retention. Frozen output is not storage-protected in this environment.",
                key);
        }

        return PutAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), idempotencyKey, "application/json", retainUntil, ct);
    }

    public async Task<T?> ReadAsync<T>(string key, CancellationToken ct = default)
    {
        await using var stream = await OpenAsync(key, ct);
        return stream is null ? default : await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
    }

    public async Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string key, CancellationToken ct = default)
    {
        await using var stream = await OpenAsync(key, ct);
        if (stream is null)
        {
            return [];
        }

        var results = new List<T>();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (JsonSerializer.Deserialize<T>(line, Json) is { } value)
            {
                results.Add(value);
            }
        }

        return results;
    }

    public async Task<string?> ReadTextAsync(string key, CancellationToken ct = default)
    {
        await using var stream = await OpenAsync(key, ct);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<ArtifactRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var metadata = await s3.GetObjectMetadataAsync(_options.Bucket, key, ct);
            var stored = metadata.Metadata[IdempotencyMetadataKey];

            if (!string.Equals(stored, idempotencyKey, StringComparison.Ordinal))
            {
                return null;
            }

            return new ArtifactRef(
                RunIdFrom(key),
                key,
                metadata.Metadata[HashMetadataKey] ?? string.Empty,
                metadata.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await s3.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await s3.DeleteObjectAsync(_options.Bucket, key, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Deleting something already gone is the outcome the caller wanted.
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix };

        do
        {
            var response = await s3.ListObjectsV2Async(request, ct);
            keys.AddRange(response.S3Objects?.Select(o => o.Key) ?? []);
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (request.ContinuationToken is not null);

        return keys;
    }

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) =>
        s3.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _options.Bucket,
            SourceKey = sourceKey,
            DestinationBucket = _options.Bucket,
            DestinationKey = destinationKey,
        }, ct);

    public string PresignPut(string key, string contentType, TimeSpan? ttl = null) =>
        s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(ttl ?? _options.UploadUrlTtl),
        });

    public string PresignGet(string key, TimeSpan? ttl = null) =>
        s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl ?? _options.DownloadUrlTtl),
        });

    private async Task<ArtifactRef> PutAsync(
        string key,
        byte[] content,
        string idempotencyKey,
        string contentType,
        DateTime? retainUntil,
        CancellationToken ct)
    {
        var hash = Ids.Sha256Hex(content);

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            ContentType = contentType,
            InputStream = new MemoryStream(content, writable: false),
        };

        request.Metadata.Add(IdempotencyMetadataKey, idempotencyKey);
        request.Metadata.Add(HashMetadataKey, hash);

        if (retainUntil is not null)
        {
            request.ObjectLockMode = ObjectLockMode.Governance;
            request.ObjectLockRetainUntilDate = retainUntil;
        }

        await s3.PutObjectAsync(request, ct);

        logger.LogDebug("Wrote artifact {Key} ({Bytes} bytes, hash {Hash})", key, content.Length, hash);
        return new ArtifactRef(RunIdFrom(key), key, hash, content.Length);
    }

    private async Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await s3.GetObjectAsync(_options.Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Recovers the run id from a <c>runs/{runId}/…</c> key, or empty for other prefixes.</summary>
    private static string RunIdFrom(string key)
    {
        if (!key.StartsWith(ArtifactKeys.RunsPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var rest = key[ArtifactKeys.RunsPrefix.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }
}
