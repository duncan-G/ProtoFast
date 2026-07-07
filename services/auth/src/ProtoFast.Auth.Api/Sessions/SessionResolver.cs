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

    private readonly SessionPolicyOptions _session = sessionOptions.Value;
    private readonly JwtSecurityTokenHandler _handler = new();

    public async Task<ResolvedIdentity?> ResolveAsync(string? cookieHeader, string? host, CancellationToken ct)
    {
        var sessionId = SessionIds.ParseCookie(cookieHeader, _session.CookieName);
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var session = await sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return null; // missing, idle-expired, or past the absolute cap
        }

        // The cookie is host-only, but defend in depth: the Host-resolved tenant must match the
        // session's realm, so a cookie can never be replayed against another tenant.
        if (!tenantResolver.TryResolve(host, out var tenant) || tenant.Realm != session.Realm)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        SessionData current;
        string? rotatedId = null;

        if (await IsAccessTokenValidAsync(session, tenant, ct).ConfigureAwait(false))
        {
            current = session;
            if (NeedsFreshJwt(current, now))
            {
                current = WithFreshJwt(current, now);
                await sessionStore.UpdateAsync(sessionId, current, ct).ConfigureAwait(false);
            }
        }
        else
        {
            // Access token is expired/invalid — try a silent refresh with the stored refresh token.
            if (session.RefreshExpiresAt <= now)
            {
                return null; // refresh token dead → session is over
            }

            KeycloakTokens refreshed;
            try
            {
                refreshed = await keycloak.RefreshAsync(tenant, session.RefreshToken, ct).ConfigureAwait(false);
            }
            catch (KeycloakException ex)
            {
                logger.LogInformation(ex, "Refresh failed for realm {Realm}; treating as anonymous", session.Realm);
                return null;
            }

            var identity = KeycloakClaims.Read(refreshed.AccessToken, refreshed.IdToken);
            current = session with
            {
                AccessToken = refreshed.AccessToken,
                RefreshToken = refreshed.RefreshToken,
                IdToken = refreshed.IdToken ?? session.IdToken,
                AccessExpiresAt = refreshed.AccessExpiresAt,
                RefreshExpiresAt = refreshed.RefreshExpiresAt,
                Roles = identity.Roles,
            };
            current = WithFreshJwt(current, now); // roles may have changed — always re-mint

            var newId = await sessionStore.ReplaceAsync(sessionId, current, ct).ConfigureAwait(false);
            rotatedId = newId == sessionId ? null : newId;
        }

        return new ResolvedIdentity(
            current.Sub,
            current.Realm,
            current.Roles,
            current.CachedInternalJwt!,
            rotatedId);
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
        var minted = jwtFactory.Create(session.Sub, session.Realm, session.Roles);
        return session with
        {
            CachedInternalJwt = minted.Token,
            InternalJwtExpiresAt = minted.ExpiresAt,
        };
    }
}
