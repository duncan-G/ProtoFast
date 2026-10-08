using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

internal sealed partial class S3ObjectStore(
    IAmazonS3 s3,
    AWSCredentials credentials,
    IOptions<S3StorageOptions> options,
    ILogger<S3ObjectStore> logger) : IObjectStore, IPresignedUrlFactory
{
    private const string IdempotencyMetadataKey = "x-amz-meta-pf-idempotency";
    private const string HashMetadataKey = "x-amz-meta-pf-hash";

    private readonly S3StorageOptions _options = options.Value;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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

    public async Task<ObjectRef?> FindExistingAsync(string key, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var metadata = await s3.GetObjectMetadataAsync(_options.Bucket, key, ct);
            var stored = metadata.Metadata[IdempotencyMetadataKey];

            if (!string.Equals(stored, idempotencyKey, StringComparison.Ordinal))
            {
                return null;
            }

            return new ObjectRef(
                key,
                metadata.Metadata[HashMetadataKey] ?? string.Empty,
                metadata.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
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


    public async Task<T?> ReadAsync<T>(string key, CancellationToken ct = default)
    {
        await using var stream = await OpenAsync(key, ct);
        return stream is null ? default : await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
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

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => OpenAsync(key, ct);

    public Task<ObjectRef> WriteAsync<T>(string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        PutAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), idempotencyKey, "application/json", null, ct);

    public Task<ObjectRef> WriteTextAsync(
        string key, string content, string idempotencyKey, string contentType = "text/plain", CancellationToken ct = default) =>
        PutAsync(key, Encoding.UTF8.GetBytes(content), idempotencyKey, contentType, null, ct);

    public Task<ObjectRef> WriteJsonLinesAsync<T>(
        string key, IEnumerable<T> values, string idempotencyKey, CancellationToken ct = default)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(JsonSerializer.Serialize(value, Json)).Append('\n');
        }

        return PutAsync(key, Encoding.UTF8.GetBytes(builder.ToString()), idempotencyKey, "application/jsonl", null, ct);
    }

    /// <summary>
    /// GOVERNANCE retention, set at write time. The instance role deliberately has no
    /// <c>s3:BypassGovernanceRetention</c>, so the worker that wrote it cannot unwrite it.
    /// </summary>
    public Task<ObjectRef> WriteFrozenAsync<T>(
        string key, T value, string idempotencyKey, CancellationToken ct = default) =>
        WriteFrozenBytesAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), "application/json", idempotencyKey, ct);

    public Task<ObjectRef> WriteFrozenBytesAsync(
        string key, byte[] content, string contentType, string idempotencyKey, CancellationToken ct = default)
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

        return PutAsync(key, content, idempotencyKey, contentType, retainUntil, ct);
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

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default) =>
        s3.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = _options.Bucket,
            SourceKey = sourceKey,
            DestinationBucket = _options.Bucket,
            DestinationKey = destinationKey,
        }, ct);

    /// <summary>
    /// The region the policy is signed for. In production the SDK resolved it (from the instance
    /// or the environment) and the client is authoritative; in development the configured value
    /// is, because that is the one LocalStack's init script created the bucket in.
    /// </summary>
    private string RegionName =>
        s3.Config.RegionEndpoint?.SystemName
        ?? (string.IsNullOrWhiteSpace(s3.Config.AuthenticationRegion) ? _options.AwsRegion : s3.Config.AuthenticationRegion)
        ?? throw new InvalidOperationException(
            "No AWS region: the S3 client resolved none and S3:AwsRegion is not configured.");

    private async Task<ObjectRef> PutAsync(
        string key,
        byte[] content,
        string idempotencyKey,
        string contentType,
        DateTime? retainUntil,
        CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(content));

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
        return new ObjectRef(key, hash, content.Length);
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
}
