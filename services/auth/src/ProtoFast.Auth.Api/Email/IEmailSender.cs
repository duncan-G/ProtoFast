namespace ProtoFast.Auth.Api.Email;

/// <summary>
/// One message, in both the parts a mail client may pick from.
/// </summary>
/// <param name="Text">The plain-text part. Always present, and written to stand on its own: it is
/// what a text-only client, a screen reader in plain-text mode, and every "view source" of the
/// message shows.</param>
/// <param name="Html">The HTML part — Nocturne, rendered by <see cref="NocturneEmail"/> so these
/// messages sit beside Keycloak's own themed mail without looking like a different product wrote
/// them. Null sends a text-only message.</param>
public sealed record EmailMessage(string To, string Subject, string Text, string? Html = null);

/// <summary>
/// The mail auth-svc sends on its own account.
///
/// <para>Keycloak still sends its own — the sign-in code, in its own templates, through its own
/// SMTP config. This exists for the mail that belongs to flows Keycloak is not running: changing
/// the address on an account is ours end to end, so proving the new address is ours too.</para>
/// </summary>
public interface IEmailSender
{
    /// <summary>Whether a relay is configured. Callers check this to answer "we can't do that
    /// right now" up front instead of failing halfway through a state change.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends the message, throwing when the relay refuses it.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
