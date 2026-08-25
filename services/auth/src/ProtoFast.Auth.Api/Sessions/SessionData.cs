namespace ProtoFast.Auth.Api.Sessions;

/// <summary>
/// The server-side session, stored as JSON in Redis under <c>sess:{sessionId}</c>. The browser
/// only ever holds the opaque <c>sessionId</c>; the Keycloak tokens never leave the server
/// (guide §3.4). The internal JWT is minted once and cached here, re-minted only near expiry.
/// </summary>
public sealed record SessionData
{
    public required string Sub { get; init; }
    public required string Email { get; init; }
    public required string Realm { get; init; }
    public required string ClientId { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }

    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }

    /// <summary>The OIDC id token, kept for <c>id_token_hint</c> on RP-initiated logout.</summary>
    public string? IdToken { get; init; }

    /// <summary>
    /// Keycloak's <c>sid</c> — the realm SSO session this one hangs off, and the handle
    /// back-channel logout arrives with. Both hosts' sessions for the same browser share it.
    /// Null for a session minted before the index existed, or from a token that carried no
    /// <c>sid</c>; those keep the old behaviour of lapsing at the next failed refresh.
    /// </summary>
    public string? KcSessionId { get; init; }

    public required DateTimeOffset AccessExpiresAt { get; init; }
    public required DateTimeOffset RefreshExpiresAt { get; init; }

    /// <summary>When the session was first created — the anchor for the absolute TTL cap.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Whether the account was subscribed when this session was created. Read from the local
    /// user row at the callback and carried into the internal JWT, so a backend never has to
    /// ask anybody: Keycloak has no opinion about subscriptions, and a claim minted there would
    /// need a standing admin credential to write. It refreshes when the session does.
    /// </summary>
    public bool Subscribed { get; init; }

    public string? CachedInternalJwt { get; init; }
    public DateTimeOffset? InternalJwtExpiresAt { get; init; }
}
