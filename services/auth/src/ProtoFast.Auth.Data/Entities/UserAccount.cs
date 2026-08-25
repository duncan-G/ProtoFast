namespace ProtoFast.Auth.Data.Entities;

/// <summary>
/// A provisioned identity — one row per (<see cref="Realm"/>, Keycloak <see cref="Subject"/>),
/// created/updated on first login (architecture doc Flow B: "upsert user in DB").
/// The browser-facing identity lives in Keycloak; this is ProtoFast's local mirror,
/// the anchor other tables reference for ownership.
/// </summary>
public sealed class UserAccount
{
    public Guid Id { get; set; }

    /// <summary>Keycloak realm the subject belongs to (e.g. <c>protofast</c>).</summary>
    public required string Realm { get; set; }

    /// <summary>Keycloak <c>sub</c> claim — stable, opaque user id within the realm.</summary>
    public required string Subject { get; set; }

    public required string Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }

    /// <summary>
    /// When this account first enrolled a passkey, or null if it never has. The passkey offer
    /// at sign-in reads it to decide whether to bother: the alternative is two redirects on
    /// every sign-in for the users who already complied.
    ///
    /// <para>It is a local mirror of a fact that lives in Keycloak, so it can drift. Two things
    /// keep it honest without a standing admin credential: the offer's own reported outcome
    /// stamps it, and a sign-in that used a passkey stamps it too — a user who authenticated
    /// with one demonstrably has one, whatever this column says.</para>
    /// </summary>
    public DateTimeOffset? PasskeyRegisteredAt { get; set; }

    /// <summary>
    /// When the account's subscription was last confirmed, or null if it has none. Written by
    /// the billing webhook; read on every sign-in callback, which is why it lives here rather
    /// than in a Keycloak token — a claim there would need a standing admin credential to write
    /// and would be stale between token refreshes anyway.
    /// </summary>
    public DateTimeOffset? SubscribedAt { get; set; }
}
