using StackExchange.Redis;

namespace ProtoFast.Auth.Api.Security;

public sealed class RedisReplayGuard(IConnectionMultiplexer redis) : IReplayGuard
{
    private const string KeyPrefix = "replay:";

    private readonly IDatabase _db = redis.GetDatabase();

    // SET NX is the whole guard: it succeeds only if the key is absent, so the claim is atomic
    // even with several auth instances behind the same Redis.
    public Task<bool> TryClaimAsync(string key, TimeSpan window, CancellationToken ct = default) =>
        _db.StringSetAsync(KeyPrefix + key, "1", window, When.NotExists);

    public Task ReleaseAsync(string key, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(KeyPrefix + key);
}
