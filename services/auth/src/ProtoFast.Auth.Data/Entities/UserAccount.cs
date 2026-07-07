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
}
