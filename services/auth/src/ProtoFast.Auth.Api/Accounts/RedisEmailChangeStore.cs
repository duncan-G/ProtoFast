using System.Text.Json;
using StackExchange.Redis;

namespace ProtoFast.Auth.Api.Accounts;

public sealed class RedisEmailChangeStore(IConnectionMultiplexer redis, TimeProvider clock) : IEmailChangeStore
{
    private const string KeyPrefix = "emailchg:";
    private const string CooldownPrefix = "emailchg:cool:";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db = redis.GetDatabase();

    public Task SaveAsync(string realm, string subject, PendingEmailChange pending, CancellationToken ct = default)
    {
        // Redis expires the key, so the TTL is the deadline: a change that outlives its own
        // ExpiresAt cannot be confirmed by a clock the caller controls, because it is not there.
        var ttl = pending.ExpiresAt - clock.GetUtcNow();
        return ttl <= TimeSpan.Zero
            ? _db.KeyDeleteAsync(Key(realm, subject))
            : _db.StringSetAsync(Key(realm, subject), JsonSerializer.Serialize(pending, JsonOptions), ttl);
    }

    public async Task<PendingEmailChange?> GetAsync(string realm, string subject, CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync(Key(realm, subject)).ConfigureAwait(false);
        return json.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<PendingEmailChange>(json.ToString(), JsonOptions);
    }

    public Task DeleteAsync(string realm, string subject, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(Key(realm, subject));

    public Task<bool> TryTakeSendSlotAsync(
        string realm, string subject, TimeSpan cooldown, CancellationToken ct = default) =>
        // SET NX with a TTL: the key's existence *is* the cooldown, and it lapses on its own.
        _db.StringSetAsync(CooldownPrefix + realm + ":" + subject, "1", cooldown, When.NotExists);

    private static string Key(string realm, string subject) => KeyPrefix + realm + ":" + subject;
}
