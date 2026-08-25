using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Security;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Telemetry;
using ProtoFast.Auth.Api.Tenancy;

namespace ProtoFast.Auth.Api.Endpoints;

/// <summary>
/// OIDC Back-Channel Logout 1.0 §2.8. Keycloak POSTs a signed logout token when a realm SSO
/// session ends — a sign-out on any host, an admin revoking the session, a password change — and
/// we erase every BFF session hanging off it.
/// <para>
/// Without this, only the host that ran <c>/signout</c> loses its session: the cookie is host-only,
/// so the sibling host keeps its own, and <see cref="SessionResolver"/> validates access tokens
/// against cached JWKS without ever asking Keycloak. Nothing notices until the access token lapses
/// and the refresh comes back <c>invalid_grant</c>, which is a window as long as the access token
/// lifespan.
/// </para>
/// <para>
/// Keycloak calls this server-to-server on the internal network. Envoy's virtual host only routes
/// the browser OIDC paths to this service, so the edge never reaches it.
/// </para>
/// </summary>
public sealed class BackchannelLogout(
    IKeycloakGateway keycloak,
    ISessionStore sessionStore,
    ITenantResolver tenantResolver,
    IReplayGuard replayGuard,
    ILogger<BackchannelLogout> logger)
{
    private const string LogoutEventClaim = "http://schemas.openid.net/event/backchannel-logout";

    /// <summary>How long a consumed <c>jti</c> is remembered. Comfortably longer than a logout
    /// token lives, so a replay can never outlast the record of the original.</summary>
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(15);

    // MapInboundClaims would rewrite `sub` to the WS-Federation URI and hide it from the lookups
    // below; the raw claim names are the ones the spec talks about.
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public async Task<IResult> HandleAsync(HttpContext ctx, CancellationToken ct)
    {
        using var activity = AuthTelemetry.Source.StartActivity("auth backchannel logout", ActivityKind.Server);

        ctx.Response.Headers.CacheControl = "no-store";

        if (!ctx.Request.HasFormContentType)
        {
            return Invalid(activity, "invalid_request", "Expected a form-encoded logout_token.");
        }

        var form = await ctx.Request.ReadFormAsync(ct);
        var rawToken = form["logout_token"].ToString();
        if (string.IsNullOrEmpty(rawToken))
        {
            return Invalid(activity, "invalid_request", "Missing logout_token.");
        }

        // Read once unverified purely to learn which realm signed it; the realm's own validation
        // parameters pin the issuer, so a forged `iss` fails at ValidateToken below.
        string? realm;
        try
        {
            realm = RealmFromIssuer(_handler.ReadJwtToken(rawToken).Issuer);
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            // Which of the two you get depends on how the token is malformed.
            return Invalid(activity, "invalid_request", "logout_token is not a JWT.");
        }

        if (realm is null)
        {
            return Invalid(activity, "invalid_request", "logout_token carries no realm issuer.");
        }

        if (!tenantResolver.KnowsRealm(realm))
        {
            // The realm still comes from an unverified token, and selecting validation parameters
            // for it means fetching that realm's keys from Keycloak. Refuse a realm we serve no
            // client in before spending the call — `https://anywhere/realms/made-up` parses as
            // cleanly as the real issuer does, and the signature check that would catch it only
            // runs after the fetch.
            return Invalid(activity, "invalid_request", "logout_token names a realm we do not serve.");
        }

        JwtSecurityToken token;
        try
        {
            var parameters = (await keycloak.GetValidationParametersAsync(realm, ct)).Clone();

            // `aud` is the client, checked against the tenant map below — the shared parameters
            // leave audience alone because access tokens carry the client in `azp` instead.
            parameters.ValidateAudience = false;

            // A logout token's `exp` is optional in the spec (Keycloak does send one). Validate it
            // when present without rejecting a token that omits it.
            parameters.RequireExpirationTime = false;

            _handler.ValidateToken(rawToken, parameters, out var validated);
            token = (JwtSecurityToken)validated;
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Rejected a logout token for realm {Realm}", realm);
            return Invalid(activity, "invalid_request", "logout_token failed validation.");
        }

        if (!IsLogoutToken(token))
        {
            // The `events` claim is the only thing separating a logout token from any other token
            // the realm signs. Without this check an id token would be accepted here.
            return Invalid(activity, "invalid_request", "logout_token carries no backchannel-logout event.");
        }

        if (Claim(token, "nonce") is not null)
        {
            // Spec §2.4: a logout token must never carry `nonce`. Its presence means someone is
            // passing an id token off as one.
            return Invalid(activity, "invalid_request", "logout_token must not carry a nonce.");
        }

        var clientId = token.Audiences.FirstOrDefault(aud => tenantResolver.TryResolveByClient(realm, aud, out _));
        if (clientId is null)
        {
            return Invalid(activity, "invalid_request", "logout_token is not addressed to a known client.");
        }

        var sid = Claim(token, "sid");
        if (string.IsNullOrEmpty(sid))
        {
            // Sessions are indexed by `sid`, so a token without one cannot be acted on. Keycloak
            // only omits it when the client's backchannel.logout.session.required is off — say so
            // loudly rather than silently accepting a logout that does nothing.
            logger.LogError(
                "Logout token for realm {Realm} client {ClientId} carried no sid; set " +
                "backchannel.logout.session.required on the client",
                realm,
                clientId);
            return Invalid(activity, "invalid_request", "logout_token carries no sid.");
        }

        var jti = Claim(token, "jti");
        var replayKey = string.IsNullOrEmpty(jti) ? null : $"bclogout:{realm}:{jti}";

        if (replayKey is not null && !await replayGuard.TryClaimAsync(replayKey, ReplayWindow, ct))
        {
            // Already handled. Erasing the sessions again would not be harmless: the user may have
            // signed in again since, and this token has no claim on the session that replaced the
            // one it ended.
            logger.LogDebug("Ignored a replayed logout token {Jti} for realm {Realm}", jti, realm);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Results.Ok();
        }

        try
        {
            await sessionStore.DeleteByKeycloakSessionAsync(realm, sid, ct);
        }
        catch
        {
            // The claim is taken but the sessions are still there, and a claim outlives the token
            // it guards — leaving it would turn every later delivery of this token into an OK that
            // erases nothing, which is the exact failure the endpoint exists to prevent. Hand it
            // back so the state on the way out matches the work actually done.
            await ReleaseClaimAsync(replayKey, realm);
            throw;
        }

        logger.LogInformation(
            "Back-channel logout cleared sessions for realm {Realm} sso session {Sid} (client {ClientId})",
            realm,
            sid,
            clientId);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return Results.Ok();
    }

    /// <summary>
    /// Gives a claimed <c>jti</c> back after the logout it guarded failed. Best-effort and
    /// deliberately swallowing: it runs on the way out of a failure, and a release that throws in
    /// turn would replace the reason the logout failed with the reason the cleanup did.
    /// <c>CancellationToken.None</c> because a cancelled request is one of the ways to get here.
    /// </summary>
    private async Task ReleaseClaimAsync(string? replayKey, string realm)
    {
        if (replayKey is null)
        {
            return;
        }

        try
        {
            await replayGuard.ReleaseAsync(replayKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not release the logout token claim {ReplayKey} for realm {Realm}; a redelivery " +
                "of that token will be treated as a replay and erase nothing",
                replayKey,
                realm);
        }
    }

    /// <summary>The realm segment of a Keycloak issuer (<c>…/realms/{realm}</c>).</summary>
    private static string? RealmFromIssuer(string? issuer)
    {
        if (string.IsNullOrEmpty(issuer) || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.Segments;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].TrimEnd('/') == "realms")
            {
                var realm = Uri.UnescapeDataString(segments[i + 1].TrimEnd('/'));
                return string.IsNullOrEmpty(realm) ? null : realm;
            }
        }

        return null;
    }

    /// <summary>Keycloak sends <c>events</c> as a JSON object keyed by event URI.</summary>
    private static bool IsLogoutToken(JwtSecurityToken token)
    {
        var events = Claim(token, "events");
        if (string.IsNullOrEmpty(events))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(events);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(LogoutEventClaim, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Claim(JwtSecurityToken token, string type) =>
        token.Claims.FirstOrDefault(c => c.Type == type)?.Value;

    private static IResult Invalid(Activity? activity, string error, string description)
    {
        activity?.SetStatus(ActivityStatusCode.Error, description);

        // Spec §2.8: a failed logout answers 400 with the OAuth-style error body.
        return Results.BadRequest(new { error, error_description = description });
    }
}
