using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Identity;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Tenancy;

namespace ProtoFast.Auth.Api.Sessions;

/// <summary>The identity a valid session yields, ready to be projected into request headers by
/// <c>Check</c>. <see cref="RotatedSessionId"/> is set only when the session id changed (refresh
/// rotation) and the cookie must be re-issued.</summary>
public sealed record ResolvedIdentity(
    string Subject,
    string Tenant,
    IReadOnlyList<string> Roles,
    string InternalJwt,
    string? RotatedSessionId);

/// <summary>
/// Turns an opaque session cookie into a verified identity (guide §3.7): load → validate the
/// Keycloak access token (JWKS/issuer/azp) → refresh on expiry → enforce tenant match → mint/reuse
/// the cached internal JWT. Returns null for anything that isn't a live, in-tenant session; it
/// never throws for the caller to translate into a deny — <c>Check</c> only ever annotates.
/// </summary>
public sealed class SessionResolver(
    ISessionStore sessionStore,
    IKeycloakGateway keycloak,
    IInternalJwtFactory jwtFactory,
    ITenantResolver tenantResolver,
    IOptions<SessionPolicyOptions> sessionOptions,
    TimeProvider clock,
    ILogger<SessionResolver> logger)
{
    // Re-mint the internal JWT a little before it lapses so the upstream never sees an expired one.
    private static readonly TimeSpan ReMintSkew = TimeSpan.FromSeconds(30);

    // How long a request waits on a sibling's refresh before giving up as anonymous; the lock
    // outlives it so a waiter never takes over from a holder that is merely slow.
    private static readonly TimeSpan RefreshWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RefreshLockExpiry = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RefreshPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly SessionPolicyOptions _session = sessionOptions.Value;
    private readonly JwtSecurityTokenHandler _handler = new();

    public Task<ResolvedIdentity?> ResolveAsync(string? cookieHeader, string? host, CancellationToken ct) =>
        ResolveSessionAsync(SessionIds.ParseCookie(cookieHeader, _session.CookieName), host, ct);

    /// <summary>Same resolution from an already-parsed session id — for the auth endpoints, which
    /// run with ext_authz off and read the cookie themselves.</summary>
    public async Task<ResolvedIdentity?> ResolveSessionAsync(string? sessionId, string? host, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var session = await sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            // Missing, idle-expired, past the absolute cap — or rotated away by a refresh this
            // request raced, which leaves a pointer to the new id.
            return await FollowSuccessorAsync(sessionId, host, ct).ConfigureAwait(false);
        }

        // The cookie is host-only, but defend in depth: the Host-resolved tenant must match the
        // session's realm, so a cookie can never be replayed against another tenant.
        if (!tenantResolver.TryResolve(host, out var tenant) || tenant.Realm != session.Realm)
        {
            return null;
        }

        if (await IsAccessTokenValidAsync(session, tenant, ct).ConfigureAwait(false))
        {
            return await LiveIdentityAsync(sessionId, session, ct).ConfigureAwait(false);
        }

        // Access token is expired/invalid — try a silent refresh with the stored refresh token.
        if (session.RefreshExpiresAt <= clock.GetUtcNow())
        {
            return await DropAsync(sessionId, ct).ConfigureAwait(false);
        }

        return await RefreshAsync(sessionId, session, tenant, host, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the session's Keycloak tokens, once. A page fires several requests together, and
    /// they all find the same expired access token; Keycloak honours a refresh token exactly once
    /// (<c>refreshTokenMaxReuse: 0</c>), so every request but the first would be refused and read
    /// as a dead session. Instead the first takes the lock and the rest wait for its result.
    /// </summary>
    private async Task<ResolvedIdentity?> RefreshAsync(
        string sessionId,
        SessionData stale,
        TenantConfig tenant,
        string? host,
        CancellationToken ct)
    {
        var deadline = clock.GetUtcNow() + RefreshWait;
        string? lockToken;
        while ((lockToken = await sessionStore.TryLockRefreshAsync(sessionId, RefreshLockExpiry, ct).ConfigureAwait(false)) is null)
        {
            if (clock.GetUtcNow() >= deadline)
            {
                // Whoever holds the lock is stuck on Keycloak: transient, so leave the session be.
                logger.LogWarning("Timed out waiting on a concurrent refresh for realm {Realm}", stale.Realm);
                return null;
            }

            await Task.Delay(RefreshPollInterval, clock, ct).ConfigureAwait(false);
        }

        try
        {
            // The holder we waited on has usually done this refresh already.
            var session = await sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
            if (session is null)
            {
                return await FollowSuccessorAsync(sessionId, host, ct).ConfigureAwait(false);
            }

            if (session.RefreshToken != stale.RefreshToken)
            {
                return await LiveIdentityAsync(sessionId, session, ct).ConfigureAwait(false);
            }

            KeycloakTokens refreshed;
            try
            {
                refreshed = await keycloak.RefreshAsync(tenant, session.RefreshToken, ct).ConfigureAwait(false);
            }
            catch (KeycloakException ex)
            {
                logger.LogInformation(ex, "Refresh failed for realm {Realm}; treating as anonymous", session.Realm);

                // Keycloak refusing the grant is final — typically the SSO session ended, which a
                // sign-out on a sibling host in the same realm does. A 5xx or transport blip is
                // transient, so leave the session alone and let the next request try again.
                return ex.IsGrantRejected
                    ? await DropAsync(sessionId, ct).ConfigureAwait(false)
                    : null;
            }

            var identity = KeycloakClaims.Read(refreshed.AccessToken, refreshed.IdToken);
            var current = session with
            {
                AccessToken = refreshed.AccessToken,
                RefreshToken = refreshed.RefreshToken,
                IdToken = refreshed.IdToken ?? session.IdToken,
                AccessExpiresAt = refreshed.AccessExpiresAt,
                RefreshExpiresAt = refreshed.RefreshExpiresAt,
                Roles = identity.Roles,
                // A refresh stays inside the same SSO session, so keep the existing sid when the
                // new token set doesn't restate it — losing it would drop the session out of the
                // back-channel logout index.
                KcSessionId = identity.SessionId ?? session.KcSessionId,
            };
            current = WithFreshJwt(current, clock.GetUtcNow()); // roles may have changed — always re-mint

            var newId = await sessionStore.ReplaceAsync(sessionId, current, ct).ConfigureAwait(false);
            return ToIdentity(current, newId == sessionId ? null : newId);
        }
        finally
        {
            await sessionStore.ReleaseRefreshLockAsync(sessionId, lockToken, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves the id a refresh rotated this one into, re-issuing it as the cookie so
    /// the browser stops sending the old one.</summary>
    private async Task<ResolvedIdentity?> FollowSuccessorAsync(string sessionId, string? host, CancellationToken ct)
    {
        var successor = await sessionStore.GetSuccessorAsync(sessionId, ct).ConfigureAwait(false);
        if (successor is null)
        {
            return null;
        }

        var identity = await ResolveSessionAsync(successor, host, ct).ConfigureAwait(false);
        return identity is null ? null : identity with { RotatedSessionId = identity.RotatedSessionId ?? successor };
    }

    private async Task<ResolvedIdentity> LiveIdentityAsync(string sessionId, SessionData session, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (NeedsFreshJwt(session, now))
        {
            session = WithFreshJwt(session, now);
            await sessionStore.UpdateAsync(sessionId, session, ct).ConfigureAwait(false);
        }

        return ToIdentity(session, rotatedSessionId: null);
    }

    private static ResolvedIdentity ToIdentity(SessionData session, string? rotatedSessionId) =>
        new(session.Sub, session.Realm, session.Roles, session.CachedInternalJwt!, rotatedSessionId);

    /// <summary>
    /// Erases a session that can never resolve again, and reports it as anonymous. Leaving the
    /// record behind is what turns a dead session into a redirect loop: the SSR gate bounces the
    /// unannotated request to /signin, and /signin sees a session still sitting in the store and
    /// bounces it straight back to the page that just failed.
    /// </summary>
    private async Task<ResolvedIdentity?> DropAsync(string sessionId, CancellationToken ct)
    {
        await sessionStore.DeleteAsync(sessionId, ct).ConfigureAwait(false);
        return null;
    }

    private async Task<bool> IsAccessTokenValidAsync(SessionData session, TenantConfig tenant, CancellationToken ct)
    {
        try
        {
            var parameters = await keycloak.GetValidationParametersAsync(session.Realm, ct).ConfigureAwait(false);
            _handler.ValidateToken(session.AccessToken, parameters, out var validated);

            // Keycloak puts the client in `azp`; reject a token minted for another client.
            var azp = (validated as JwtSecurityToken)?.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
            return string.Equals(azp, tenant.ClientId, StringComparison.Ordinal);
        }
        catch (SecurityTokenException)
        {
            // Expired/invalid/unknown-kid → let the refresh path try.
            return false;
        }
    }

    private bool NeedsFreshJwt(SessionData session, DateTimeOffset now) =>
        string.IsNullOrEmpty(session.CachedInternalJwt)
        || session.InternalJwtExpiresAt is null
        || session.InternalJwtExpiresAt.Value <= now + ReMintSkew;

    private SessionData WithFreshJwt(SessionData session, DateTimeOffset now)
    {
        _ = now;
        var minted = jwtFactory.Create(session.Sub, session.Realm, session.Roles, session.Subscribed);
        return session with
        {
            CachedInternalJwt = minted.Token,
            InternalJwtExpiresAt = minted.ExpiresAt,
        };
    }
}
