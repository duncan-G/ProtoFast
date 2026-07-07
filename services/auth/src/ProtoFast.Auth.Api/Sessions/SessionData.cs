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

    public required DateTimeOffset AccessExpiresAt { get; init; }
    public required DateTimeOffset RefreshExpiresAt { get; init; }

    /// <summary>When the session was first created — the anchor for the absolute TTL cap.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    public string? CachedInternalJwt { get; init; }
    public DateTimeOffset? InternalJwtExpiresAt { get; init; }
}
