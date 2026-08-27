using System.Text.Json;
using StackExchange.Redis;

namespace ProtoFast.Auth.Api.Accounts;

public sealed class RedisEmailChangeStore(IConnectionMultiplexer redis, TimeProvider clock) : IEmailChangeStore
{
    private const string KeyPrefix = "emailchg:";
    private const string CooldownPrefix = "emailchg:cool:";
    private const string SendsPrefix = "emailchg:n:";

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

    public async Task<bool> TryTakeSendSlotAsync(
        string realm, string subject, string newEmail, TimeSpan cooldown, CancellationToken ct = default)
    {
        // Per mailbox, not per account: cancelling and typing a different address is a typo
        // fix, not a licence to keep writing to the inbox that just got a code.
        var mailbox = MailboxCooldownKey(realm, newEmail);
        if (!await _db.StringSetAsync(mailbox, "1", cooldown, When.NotExists).ConfigureAwait(false))
        {
            return false;
        }

        var sendsKey = AccountSendsKey(realm, subject);
        var n = await _db.StringIncrementAsync(sendsKey).ConfigureAwait(false);
        if (n == 1)
        {
            await _db.KeyExpireAsync(sendsKey, EmailChangeCode.SendWindow).ConfigureAwait(false);
        }

        if (n <= EmailChangeCode.MaxSendsPerWindow)
        {
            return true;
        }

        await _db.KeyDeleteAsync(mailbox).ConfigureAwait(false);
        await _db.StringDecrementAsync(sendsKey).ConfigureAwait(false);
        return false;
    }

    public async Task ReleaseSendSlotAsync(
        string realm, string subject, string newEmail, CancellationToken ct = default)
    {
        await _db.KeyDeleteAsync(MailboxCooldownKey(realm, newEmail)).ConfigureAwait(false);
        var n = await _db.StringDecrementAsync(AccountSendsKey(realm, subject)).ConfigureAwait(false);
        if (n <= 0)
        {
            await _db.KeyDeleteAsync(AccountSendsKey(realm, subject)).ConfigureAwait(false);
        }
    }

    private static string Key(string realm, string subject) => KeyPrefix + realm + ":" + subject;

    private static string MailboxCooldownKey(string realm, string email) =>
        CooldownPrefix + realm + ":" + email;

    private static string AccountSendsKey(string realm, string subject) =>
        SendsPrefix + realm + ":" + subject;
}
