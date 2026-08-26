namespace ProtoFast.Auth.Api.Accounts;

/// <summary>
/// Where a pending email change waits for its code. One per account, so asking again simply
/// replaces the last one — a user who mistyped the address does not have to wait out the old
/// request, and there is never a second live code to guess at.
/// </summary>
public interface IEmailChangeStore
{
    /// <summary>Writes the pending change, expiring it at <see cref="PendingEmailChange.ExpiresAt"/>.</summary>
    Task SaveAsync(string realm, string subject, PendingEmailChange pending, CancellationToken ct = default);

    Task<PendingEmailChange?> GetAsync(string realm, string subject, CancellationToken ct = default);

    Task DeleteAsync(string realm, string subject, CancellationToken ct = default);

    /// <summary>
    /// Takes the per-account send cooldown. False means one was mailed moments ago, and the
    /// caller must not mail another — the recipient did not ask to be written to at all.
    /// </summary>
    Task<bool> TryTakeSendSlotAsync(string realm, string subject, TimeSpan cooldown, CancellationToken ct = default);
}
