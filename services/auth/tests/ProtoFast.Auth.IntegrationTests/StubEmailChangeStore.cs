using System.Collections.Concurrent;
using ProtoFast.Auth.Api.Accounts;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>In-memory <see cref="IEmailChangeStore"/> so the account endpoints need no Redis.
/// Expiry is not modelled: nothing here holds a pending change long enough to care.</summary>
public sealed class StubEmailChangeStore : IEmailChangeStore
{
    private readonly ConcurrentDictionary<string, PendingEmailChange> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cooldowns = new(StringComparer.Ordinal);

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
        string realm, string subject, TimeSpan cooldown, CancellationToken ct = default) =>
        Task.FromResult(_cooldowns.TryAdd(Key(realm, subject), 0));

    private static string Key(string realm, string subject) => realm + ":" + subject;
}
