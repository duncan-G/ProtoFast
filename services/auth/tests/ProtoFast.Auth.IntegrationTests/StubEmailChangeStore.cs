using System.Collections.Concurrent;
using ProtoFast.Auth.Api.Accounts;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>In-memory <see cref="IEmailChangeStore"/> so the account endpoints need no Redis.
/// Expiry is not modelled: nothing here holds a pending change long enough to care.</summary>
public sealed class StubEmailChangeStore : IEmailChangeStore
{
    private readonly ConcurrentDictionary<string, PendingEmailChange> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _mailboxCooldowns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _accountSends = new(StringComparer.Ordinal);

    public Task SaveAsync(string realm, string subject, PendingEmailChange pending, CancellationToken ct = default)
    {
        _pending[Key(realm, subject)] = pending;
        return Task.CompletedTask;
    }

    public Task<PendingEmailChange?> GetAsync(string realm, string subject, CancellationToken ct = default) =>
        Task.FromResult(_pending.GetValueOrDefault(Key(realm, subject)));

    public Task DeleteAsync(string realm, string subject, CancellationToken ct = default)
    {
        _pending.TryRemove(Key(realm, subject), out _);
        return Task.CompletedTask;
    }

    public Task<bool> TryTakeSendSlotAsync(
        string realm, string subject, string newEmail, TimeSpan cooldown, CancellationToken ct = default)
    {
        if (!_mailboxCooldowns.TryAdd(MailboxKey(realm, newEmail), 0))
        {
            return Task.FromResult(false);
        }

        var n = _accountSends.AddOrUpdate(AccountKey(realm, subject), 1, (_, count) => count + 1);
        if (n <= EmailChangeCode.MaxSendsPerWindow)
        {
            return Task.FromResult(true);
        }

        _mailboxCooldowns.TryRemove(MailboxKey(realm, newEmail), out _);
        _accountSends.AddOrUpdate(AccountKey(realm, subject), 0, (_, count) => count - 1);
        return Task.FromResult(false);
    }

    public Task ReleaseSendSlotAsync(
        string realm, string subject, string newEmail, CancellationToken ct = default)
    {
        _mailboxCooldowns.TryRemove(MailboxKey(realm, newEmail), out _);
        _accountSends.AddOrUpdate(AccountKey(realm, subject), 0, (_, count) => Math.Max(0, count - 1));
        return Task.CompletedTask;
    }

    private static string Key(string realm, string subject) => realm + ":" + subject;

    private static string MailboxKey(string realm, string email) => "cool:" + realm + ":" + email;

    private static string AccountKey(string realm, string subject) => "n:" + realm + ":" + subject;
}
