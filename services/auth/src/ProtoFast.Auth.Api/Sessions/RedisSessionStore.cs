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

    /// <summary>Keycloak SSO session → the session ids hanging off it. A set, not a single id:
    /// every host the browser signed into shares one <c>sid</c>.</summary>
    private const string IndexPrefix = "kcsid:";

    /// <summary>Old id → the id a refresh rotated it into, for the rotation grace.</summary>
    private const string SuccessorPrefix = "sess-next:";

    private const string RefreshLockPrefix = "sess-refresh:";

    // Requests already in flight with the old cookie follow it to the new id for this long.
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db = redis.GetDatabase();
    private readonly SessionPolicyOptions _options = options.Value;

    public async Task<string> CreateAsync(SessionData data, CancellationToken ct = default)
    {
        var id = SessionIds.Generate();
        await _db.StringSetAsync(Key(id), Serialize(data), TtlFor(data.CreatedAt));
        await ReindexAsync(data, add: id, remove: null);
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

    // The index member is left behind: deleting a session id that is already gone is a no-op, so a
    // stale member costs one wasted DEL at logout instead of a read-before-delete on every one.
    public Task DeleteAsync(string sessionId, CancellationToken ct = default) =>
        string.IsNullOrEmpty(sessionId) ? Task.CompletedTask : _db.KeyDeleteAsync(Key(sessionId));

    public async Task DeleteByKeycloakSessionAsync(string realm, string kcSessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(realm) || string.IsNullOrEmpty(kcSessionId))
        {
            return;
        }

        var indexKey = IndexKey(realm, kcSessionId);
        var members = await _db.SetMembersAsync(indexKey);

        // Sessions first, index second: dropping the index first would strand every member if the
        // delete below failed, leaving sessions alive that nothing can find again.
        if (members.Length > 0)
        {
            await _db.KeyDeleteAsync(Array.ConvertAll(members, m => (RedisKey)Key(m.ToString())));
        }

        await _db.KeyDeleteAsync(indexKey);
    }

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
        await ReindexAsync(data, add: newId, remove: oldSessionId);

        if (!string.IsNullOrEmpty(oldSessionId))
        {
            // The pointer goes in before the old record comes out, so a request that misses the
            // record always finds the pointer. Keeping the old record instead would leave its
            // spent tokens for the next request to refresh again, which Keycloak refuses.
            await _db.StringSetAsync(SuccessorKey(oldSessionId), newId, RotationGrace);
            await _db.KeyDeleteAsync(Key(oldSessionId));
        }

        return newId;
    }

    public async Task<string?> GetSuccessorAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var successor = await _db.StringGetAsync(SuccessorKey(sessionId));
        return successor.IsNullOrEmpty ? null : successor.ToString();
    }

    public async Task<string?> TryLockRefreshAsync(string sessionId, TimeSpan expiry, CancellationToken ct = default)
    {
        var lockToken = Guid.NewGuid().ToString("N");
        return await _db.LockTakeAsync(RefreshLockKey(sessionId), lockToken, expiry) ? lockToken : null;
    }

    public Task ReleaseRefreshLockAsync(string sessionId, string lockToken, CancellationToken ct = default) =>
        _db.LockReleaseAsync(RefreshLockKey(sessionId), lockToken);

    /// <summary>
    /// Points the SSO-session index at the session's current id. Expiry tracks the absolute cap
    /// rather than the idle window on purpose — <see cref="GetAsync"/> slides each session's own
    /// TTL, so an index on the idle window would lapse under a session still in daily use and
    /// quietly take back-channel logout with it.
    /// </summary>
    private async Task ReindexAsync(SessionData data, string? add, string? remove)
    {
        if (string.IsNullOrEmpty(data.KcSessionId))
        {
            return;
        }

        var key = IndexKey(data.Realm, data.KcSessionId);

        if (!string.IsNullOrEmpty(remove))
        {
            await _db.SetRemoveAsync(key, remove);
        }

        if (!string.IsNullOrEmpty(add))
        {
            await _db.SetAddAsync(key, add);
        }

        var ttl = RemainingToCap(data.CreatedAt);
        if (ttl > TimeSpan.Zero)
        {
            await _db.KeyExpireAsync(key, ttl);
        }
    }

    private static string Key(string sessionId) => KeyPrefix + sessionId;

    private static string SuccessorKey(string sessionId) => SuccessorPrefix + sessionId;

    private static string RefreshLockKey(string sessionId) => RefreshLockPrefix + sessionId;

    private static string IndexKey(string realm, string kcSessionId) =>
        $"{IndexPrefix}{realm}:{kcSessionId}";

    private static string Serialize(SessionData data) => JsonSerializer.Serialize(data, JsonOptions);

    private static SessionData? Deserialize(string json) => JsonSerializer.Deserialize<SessionData>(json, JsonOptions);

    /// <summary>Idle TTL for a write — never less than a second, never past the absolute cap.</summary>
    private TimeSpan TtlFor(DateTimeOffset createdAt) => IdleTtl(createdAt) ?? TimeSpan.FromSeconds(1);

    /// <summary>The sliding idle TTL clamped to the remaining absolute lifetime, or null if the
    /// absolute cap is already exceeded.</summary>
    private TimeSpan? IdleTtl(DateTimeOffset createdAt)
    {
        var remainingToCap = RemainingToCap(createdAt);
        if (remainingToCap <= TimeSpan.Zero)
        {
            return null;
        }

        return remainingToCap < _options.IdleTtl ? remainingToCap : _options.IdleTtl;
    }

    private TimeSpan RemainingToCap(DateTimeOffset createdAt) =>
        _options.AbsoluteTtl - (clock.GetUtcNow() - createdAt);
}
