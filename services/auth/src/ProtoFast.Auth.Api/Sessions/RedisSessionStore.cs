using System.Text.Json;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using StackExchange.Redis;

namespace ProtoFast.Auth.Api.Sessions;

/// <summary>
/// Redis-backed <see cref="ISessionStore"/>. The key TTL is the sliding idle window, reset on
/// every read; it is clamped so it never outlives the absolute cap measured from
/// <see cref="SessionData.CreatedAt"/> (guide §3.4).
/// </summary>
public sealed class RedisSessionStore(
    IConnectionMultiplexer redis,
    IOptions<SessionPolicyOptions> options,
    TimeProvider clock) : ISessionStore
{
    private const string KeyPrefix = "sess:";

    // Old ids linger briefly after rotation so concurrent in-flight requests don't fail.
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db = redis.GetDatabase();
    private readonly SessionPolicyOptions _options = options.Value;

    public async Task<string> CreateAsync(SessionData data, CancellationToken ct = default)
    {
        var id = SessionIds.Generate();
        await _db.StringSetAsync(Key(id), Serialize(data), TtlFor(data.CreatedAt));
        return id;
    }

    public async Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var key = Key(sessionId);
        var json = await _db.StringGetAsync(key);
        if (json.IsNullOrEmpty)
        {
            return null;
        }

        var data = Deserialize(json.ToString());
        if (data is null)
        {
            return null;
        }

        var ttl = IdleTtl(data.CreatedAt);
        if (ttl is null)
        {
            // Absolute cap exceeded — kill the warm key and force full re-auth.
            await _db.KeyDeleteAsync(key);
            return null;
        }

        await _db.KeyExpireAsync(key, ttl.Value); // slide the idle window
        return data;
    }

    public Task DeleteAsync(string sessionId, CancellationToken ct = default) =>
        string.IsNullOrEmpty(sessionId) ? Task.CompletedTask : _db.KeyDeleteAsync(Key(sessionId));

    public Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default) =>
        _db.StringSetAsync(Key(sessionId), Serialize(data), TtlFor(data.CreatedAt));

    public async Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default)
    {
        if (!_options.RotateIdOnRefresh)
        {
            await _db.StringSetAsync(Key(oldSessionId), Serialize(data), TtlFor(data.CreatedAt));
            return oldSessionId;
        }

        var newId = SessionIds.Generate();
        await _db.StringSetAsync(Key(newId), Serialize(data), TtlFor(data.CreatedAt));

        if (!string.IsNullOrEmpty(oldSessionId))
        {
            await _db.KeyExpireAsync(Key(oldSessionId), RotationGrace);
        }

        return newId;
    }

    private static string Key(string sessionId) => KeyPrefix + sessionId;

    private static string Serialize(SessionData data) => JsonSerializer.Serialize(data, JsonOptions);

    private static SessionData? Deserialize(string json) => JsonSerializer.Deserialize<SessionData>(json, JsonOptions);

    /// <summary>Idle TTL for a write — never less than a second, never past the absolute cap.</summary>
    private TimeSpan TtlFor(DateTimeOffset createdAt) => IdleTtl(createdAt) ?? TimeSpan.FromSeconds(1);

    /// <summary>The sliding idle TTL clamped to the remaining absolute lifetime, or null if the
    /// absolute cap is already exceeded.</summary>
    private TimeSpan? IdleTtl(DateTimeOffset createdAt)
    {
        var remainingToCap = _options.AbsoluteTtl - (clock.GetUtcNow() - createdAt);
        if (remainingToCap <= TimeSpan.Zero)
        {
            return null;
        }

        return remainingToCap < _options.IdleTtl ? remainingToCap : _options.IdleTtl;
    }
}
