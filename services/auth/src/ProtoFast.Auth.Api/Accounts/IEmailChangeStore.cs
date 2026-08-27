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

    /// <summary>Clears the parked change. Send limits stay: they are about mail that already
    /// left, not about whether the user still wants the change.</summary>
    Task DeleteAsync(string realm, string subject, CancellationToken ct = default);

    /// <summary>
    /// Takes a send slot for <paramref name="newEmail"/>. False means that mailbox was mailed
    /// moments ago, or this account has spent its window. Cancel does not give the slot back.
    /// </summary>
    Task<bool> TryTakeSendSlotAsync(
        string realm, string subject, string newEmail, TimeSpan cooldown, CancellationToken ct = default);

    /// <summary>
    /// Returns a slot taken for a send that never left. A failed SMTP handoff must not leave
    /// the user waiting on mail that was never accepted.
    /// </summary>
    Task ReleaseSendSlotAsync(
        string realm, string subject, string newEmail, CancellationToken ct = default);
}
