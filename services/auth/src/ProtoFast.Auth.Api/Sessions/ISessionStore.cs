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

    /// <summary>Rewrites a session in place (same id), sliding the idle window — used to cache the
    /// re-minted internal JWT without rotating the cookie.</summary>
    Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default);

    /// <summary>Rewrites a session after a token refresh. When rotation is enabled, writes under a
    /// new id (returned) and lets the old id lapse after a short grace; otherwise updates in place.</summary>
    Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default);
}
