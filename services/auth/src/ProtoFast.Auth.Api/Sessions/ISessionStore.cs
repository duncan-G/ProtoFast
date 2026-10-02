namespace ProtoFast.Auth.Api.Sessions;

/// <summary>Opaque-id → Redis session CRUD. The lifetime policy (sliding idle, absolute cap,
/// rotation) lives in the implementation (guide §3.4).</summary>
public interface ISessionStore
{
    /// <summary>Persists a new session and returns its freshly generated opaque id.</summary>
    Task<string> CreateAsync(SessionData data, CancellationToken ct = default);

    /// <summary>Loads a session, sliding its idle window. Returns null if missing or past the
    /// absolute cap (in which case the key is removed).</summary>
    Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default);

    Task DeleteAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Erases every session hanging off a Keycloak SSO session — the back-channel logout path.
    /// Cookies are host-only, so admin and the web app hold separate sessions that share one
    /// <c>sid</c>; this is what lets a sign-out (or an admin revoking the session in Keycloak)
    /// reach both instead of waiting for each to fail its next refresh.
    /// </summary>
    Task DeleteByKeycloakSessionAsync(string realm, string kcSessionId, CancellationToken ct = default);

    /// <summary>Rewrites a session in place (same id), sliding the idle window — used to cache the
    /// re-minted internal JWT without rotating the cookie.</summary>
    Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default);

    /// <summary>Rewrites a session after a token refresh. When rotation is enabled, writes under a
    /// new id (returned), removes the old one and leaves a short-lived pointer from it to the new
    /// one (<see cref="GetSuccessorAsync"/>); otherwise updates in place.</summary>
    Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default);

    /// <summary>The id a refresh rotated <paramref name="sessionId"/> into, while the rotation
    /// grace lasts — for requests that were already carrying the old cookie.</summary>
    Task<string?> GetSuccessorAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Takes the session's refresh lock, so one request refreshes and its siblings wait for the
    /// result instead of spending the same single-use refresh token. Returns the token to release
    /// it with, or null while someone else holds it. The lock expires on its own after
    /// <paramref name="expiry"/> in case its holder dies.
    /// </summary>
    Task<string?> TryLockRefreshAsync(string sessionId, TimeSpan expiry, CancellationToken ct = default);

    Task ReleaseRefreshLockAsync(string sessionId, string lockToken, CancellationToken ct = default);
}
