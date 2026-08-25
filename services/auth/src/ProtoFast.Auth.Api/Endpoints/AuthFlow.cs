using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Correlation;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Security;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Telemetry;
using ProtoFast.Auth.Api.Tenancy;
using ProtoFast.Auth.Data;
using ProtoFast.Auth.Data.Entities;

namespace ProtoFast.Auth.Api.Endpoints;

/// <summary>
/// Drives the browser OIDC flow (guide §3.6): kicks off Authorization-Code-with-PKCE, handles the
/// callback (code → tokens, provision, session cookie), and signs out. Scoped — it holds the
/// request's <see cref="AuthDbContext"/>.
/// </summary>
public sealed class AuthFlow(
    ITenantResolver tenantResolver,
    IKeycloakGateway keycloak,
    ICorrelationStore correlationStore,
    ISessionStore sessionStore,
    SessionResolver sessionResolver,
    AuthDbContext db,
    IOptions<SessionPolicyOptions> sessionOptions,
    IOptions<SubscriptionOptions> subscriptionOptions,
    TimeProvider clock,
    ILogger<AuthFlow> logger)
{
    private readonly SessionPolicyOptions _session = sessionOptions.Value;
    private readonly SubscriptionOptions _subscriptions = subscriptionOptions.Value;

    /// <summary>/signin, /signup (registration), /add-passkey — set up correlation, 302 to Keycloak.</summary>
    /// <param name="skipIfAuthenticated">When true (sign-in/sign-up), an already-valid session
    /// short-circuits straight to the return target instead of re-running the flow. An endpoint
    /// whose whole purpose is to reach Keycloak — /add-passkey — has to switch this off, or it
    /// bounces off the very session it depends on and never gets there.</param>
    /// <param name="kcAction">An Application Initiated Action to run inside the authorize request.
    /// The correlation records that this round trip carried the offer, so the callback reports
    /// its outcome rather than chaining a second one.</param>
    public async Task<IResult> StartAsync(
        HttpContext ctx,
        bool registration,
        CancellationToken ct,
        bool skipIfAuthenticated = true,
        string? kcAction = null)
    {
        // These endpoints run with ext_authz OFF, so identity isn't injected — but the session
        // cookie still rides along. If it resolves to a live session the user is already signed in;
        // bounce them to the return target (defaults to /app) rather than looping back to Keycloak.
        if (skipIfAuthenticated && await IsSignedInAsync(ctx, ct))
        {
            return Results.Redirect(SafeReturnUrl(ctx.Request.Query["returnUrl"]));
        }

        if (!tenantResolver.TryResolve(ctx.Request.Host.Value, out var tenant))
        {
            // Unknown host → never guess a realm.
            return Results.NotFound();
        }

        var redirectUri = Origin(ctx) + "/signin-oidc";
        var returnUrl = SafeReturnUrl(ctx.Request.Query["returnUrl"]);
        var (verifier, challenge) = Pkce.Create();
        var state = SessionIds.Generate();

        // The auto-instrumented request span is this trace's root. Stash its traceparent so the
        // callback (a separate request Keycloak starts a fresh trace for) can rejoin this trace.
        Activity.Current?.SetTag("auth.flow", registration ? "sign-up" : "sign-in");
        Activity.Current?.SetTag("auth.realm", tenant.Realm);

        await correlationStore.SaveAsync(
            state,
            new CorrelationData(
                verifier,
                redirectUri,
                returnUrl,
                tenant.Realm,
                tenant.ClientId,
                Activity.Current?.Id ?? "",
                PasskeyOffer: kcAction == KeycloakActions.RegisterPasskey),
            ct);

        return Results.Redirect(keycloak.BuildAuthorizeUrl(tenant, redirectUri, state, challenge, registration, kcAction));
    }

    /// <summary>/signin-oidc — verify state, exchange code, provision, issue the session cookie.</summary>
    public async Task<IResult> CallbackAsync(HttpContext ctx, CancellationToken ct)
    {
        var query = ctx.Request.Query;
        var state = query["state"].ToString();

        // Resolve correlation up front: it is the single-use CSRF guard and carries the traceparent
        // of the /signin request. Keycloak's callback redirect starts a fresh request (whose auto
        // span is suppressed in ServiceDefaults), so we open our own span parented to that stashed
        // context — rejoining the sign-in trace on success, or a standalone root span on failure.
        var correlation = string.IsNullOrEmpty(state) ? null : await correlationStore.TakeAsync(state, ct);
        ActivityContext.TryParse(correlation?.Traceparent ?? "", null, isRemote: true, out var parentCtx);
        using var activity = AuthTelemetry.Source.StartActivity("auth sign-in callback", ActivityKind.Server, parentCtx);

        if (query.TryGetValue("error", out var error))
        {
            logger.LogWarning("OIDC callback returned error {Error}", error.ToString());
            activity?.SetStatus(ActivityStatusCode.Error, error.ToString());
            return Results.Redirect("/");
        }

        var code = query["code"].ToString();
        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(code))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing state or code");
            return Results.BadRequest();
        }

        if (correlation is null)
        {
            // Unknown/expired/replayed state — the CSRF guard.
            activity?.SetStatus(ActivityStatusCode.Error, "Unknown or expired state");
            return Results.BadRequest();
        }

        var tenant = new TenantConfig { Realm = correlation.Realm, ClientId = correlation.ClientId };

        KeycloakTokens tokens;
        try
        {
            tokens = await keycloak.ExchangeCodeAsync(tenant, code, correlation.RedirectUri, correlation.CodeVerifier, ct);
        }
        catch (KeycloakException ex)
        {
            logger.LogError(ex, "Token exchange failed for realm {Realm}", correlation.Realm);
            activity?.SetStatus(ActivityStatusCode.Error, "Token exchange failed");
            return Results.Redirect("/");
        }

        var identity = KeycloakClaims.Read(tokens.AccessToken, tokens.IdToken);
        if (string.IsNullOrEmpty(identity.Subject))
        {
            logger.LogError("Access token for realm {Realm} carried no subject", correlation.Realm);
            activity?.SetStatus(ActivityStatusCode.Error, "Access token carried no subject");
            return Results.Redirect("/");
        }

        // acr_values is a request, not a guarantee: a realm that has not been given the step-up
        // branch happily issues a token at whatever level it did reach. Gating the admin console
        // on a level we merely asked for would be no gate at all, so check what came back.
        var hostTenant = HostTenant(ctx, correlation);
        if (hostTenant.AcrValues is { Length: > 0 } required
            && !string.Equals(identity.Acr, required, StringComparison.Ordinal))
        {
            logger.LogError(
                "Realm {Realm} returned acr {Acr} for client {ClientId}; {Required} was required",
                correlation.Realm, identity.Acr ?? "(none)", correlation.ClientId, required);
            activity?.SetStatus(ActivityStatusCode.Error, "Required acr not satisfied");
            return Results.Redirect("/");
        }

        var user = await ProvisionAsync(correlation.Realm, identity, ct);

        var now = clock.GetUtcNow();
        var session = new SessionData
        {
            Sub = identity.Subject,
            Email = identity.Email,
            Realm = correlation.Realm,
            ClientId = correlation.ClientId,
            Roles = identity.Roles,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            IdToken = tokens.IdToken,
            AccessExpiresAt = tokens.AccessExpiresAt,
            RefreshExpiresAt = tokens.RefreshExpiresAt,
            CreatedAt = now,
            KcSessionId = identity.SessionId,
            Subscribed = user.SubscribedAt is not null,
        };

        // A callback that lands while a session cookie is already live replaces it, which is
        // routine for /reset and unavoidable for the passkey offer — that second leg is a
        // whole authorize round trip and comes back with a fresh token set. Drop the record
        // the cookie is about to stop pointing at, or every offer leaves one behind in Redis
        // until its TTL runs out.
        var previousSessionId = ctx.Request.Cookies[_session.CookieName];

        var sessionId = await sessionStore.CreateAsync(session, ct);
        AppendSessionCookie(ctx, sessionId);

        if (!string.IsNullOrEmpty(previousSessionId) && previousSessionId != sessionId)
        {
            await sessionStore.DeleteAsync(previousSessionId, ct);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);

        // This round trip carried the passkey offer — sign-up, where it rides along on the one
        // authorize request, or the second leg chained off a sign-in. Record what happened.
        // Cancelling is a legitimate answer — the user is asked again next time they sign in,
        // and session lifetime is the whole of the cadence.
        if (correlation.PasskeyOffer)
        {
            var status = query[KeycloakActions.StatusParameter].ToString();
            if (string.Equals(status, KeycloakActions.StatusSuccess, StringComparison.OrdinalIgnoreCase))
            {
                await StampPasskeyAsync(user, now, ct);
            }

            activity?.SetTag("auth.passkey_offer_status", string.IsNullOrEmpty(status) ? "none" : status);
        }

        // An account that has to subscribe hands off to Angular, because the subscription
        // workflow is Angular's and takes minutes, a payment redirect and a webhook. Both
        // branches end at the same doorway; Angular just decides when.
        if (_subscriptions.Enabled && user.SubscribedAt is null)
        {
            return Results.Redirect(WithFlag(correlation.ReturnUrl, _subscriptions.ReturnUrlFlag));
        }

        // Already offered on this trip; never chain a second one.
        if (correlation.PasskeyOffer)
        {
            return Results.Redirect(correlation.ReturnUrl);
        }

        return await OfferPasskeyOrReturnAsync(ctx, correlation, hostTenant, user, identity, now, ct);
    }

    /// <summary>
    /// Chains the passkey offer onto the sign-in that just completed. Returns straight to the
    /// app instead when the account already has one. Sign-up does not come through here — it
    /// carries the offer on its own authorize request, because the session registration leaves
    /// behind cannot satisfy a follow-up one (see <c>/signup</c> in <see cref="AuthEndpoints"/>).
    /// </summary>
    private async Task<IResult> OfferPasskeyOrReturnAsync(
        HttpContext ctx,
        CorrelationData correlation,
        TenantConfig tenant,
        UserAccount user,
        KeycloakIdentity identity,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // A sign-in that used a passkey is proof of one, whatever the local column says. Repair
        // it here and the round trip below is skipped from now on — this is what covers a
        // credential added through Keycloak's own account console.
        if (identity.AuthenticatedWithPasskey && user.PasskeyRegisteredAt is null)
        {
            await StampPasskeyAsync(user, now, ct);
        }

        if (user.PasskeyRegisteredAt is not null)
        {
            return Results.Redirect(correlation.ReturnUrl);
        }

        var redirectUri = Origin(ctx) + "/signin-oidc";
        var (verifier, challenge) = Pkce.Create();
        var state = SessionIds.Generate();

        await correlationStore.SaveAsync(
            state,
            new CorrelationData(
                verifier,
                redirectUri,
                correlation.ReturnUrl,
                tenant.Realm,
                tenant.ClientId,
                Activity.Current?.Id ?? "",
                PasskeyOffer: true),
            ct);

        return Results.Redirect(keycloak.BuildAuthorizeUrl(
            tenant, redirectUri, state, challenge, registration: false, KeycloakActions.RegisterPasskey));
    }

    private async Task StampPasskeyAsync(UserAccount user, DateTimeOffset now, CancellationToken ct)
    {
        user.PasskeyRegisteredAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The full tenant config for the host this callback arrived on, which carries the per-client
    /// re-authentication policy the correlation record does not. Falls back to realm and client
    /// alone if the host no longer maps anywhere, or maps somewhere else — the correlation is the
    /// authority on which client this flow belongs to.
    /// </summary>
    private TenantConfig HostTenant(HttpContext ctx, CorrelationData correlation) =>
        tenantResolver.TryResolve(ctx.Request.Host.Value, out var tenant)
        && tenant.Realm == correlation.Realm
        && tenant.ClientId == correlation.ClientId
            ? tenant
            : new TenantConfig { Realm = correlation.Realm, ClientId = correlation.ClientId };

    private static string WithFlag(string returnUrl, string flag) =>
        returnUrl + (returnUrl.Contains('?') ? '&' : '?') + Uri.EscapeDataString(flag) + "=1";

    /// <summary>/signout — drop the session, clear the cookie, 302 to Keycloak end-session.</summary>
    public async Task<IResult> SignOutAsync(HttpContext ctx, CancellationToken ct)
    {
        var sessionId = ctx.Request.Cookies[_session.CookieName];
        string? idTokenHint = null;

        if (!string.IsNullOrEmpty(sessionId))
        {
            // The session may already be gone (dropped when its tokens died), so the id_token_hint
            // is best-effort — the end-session URL carries client_id either way.
            idTokenHint = (await sessionStore.GetAsync(sessionId, ct))?.IdToken;
            await sessionStore.DeleteAsync(sessionId, ct);
        }

        ClearSessionCookie(ctx);

        if (!tenantResolver.TryResolve(ctx.Request.Host.Value, out var tenant))
        {
            return Results.Redirect("/");
        }

        return Results.Redirect(keycloak.BuildEndSessionUrl(tenant, idTokenHint, Origin(ctx) + "/"));
    }

    /// <summary>
    /// Is the caller already signed in? Resolve the cookie exactly as <c>Check</c> would — a
    /// session must still yield an identity, not merely have a record in the store. Answering
    /// "yes" for a session whose tokens are dead is a redirect loop: the SSR gate sends every
    /// unauthenticated request here, and here we send it straight back.
    /// </summary>
    private async Task<bool> IsSignedInAsync(HttpContext ctx, CancellationToken ct)
    {
        var sessionId = ctx.Request.Cookies[_session.CookieName];
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        ResolvedIdentity? identity;
        try
        {
            identity = await sessionResolver.ResolveSessionAsync(sessionId, ctx.Request.Host.Value, ct);
        }
        catch (Exception ex)
        {
            // Redis or Keycloak is unreachable. A fresh sign-in is the safe answer — it fails
            // visibly at Keycloak instead of ping-ponging the browser.
            logger.LogWarning(ex, "Session resolution failed during sign-in; starting a fresh flow");
            return false;
        }

        if (identity is null)
        {
            // The resolver has already dropped the record if it was unrecoverable; drop the cookie
            // too so the browser stops replaying an id that can never work again.
            ClearSessionCookie(ctx);
            return false;
        }

        if (identity.RotatedSessionId is not null)
        {
            AppendSessionCookie(ctx, identity.RotatedSessionId);
        }

        return true;
    }

    private async Task<UserAccount> ProvisionAsync(string realm, KeycloakIdentity identity, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Realm == realm && u.Subject == identity.Subject, ct);
        var now = clock.GetUtcNow();

        if (user is null)
        {
            user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Realm = realm,
                Subject = identity.Subject,
                Email = identity.Email,
                CreatedAt = now,
                LastLoginAt = now,
            };
            db.Users.Add(user);
        }
        else
        {
            user.Email = identity.Email;
            user.LastLoginAt = now;
        }

        await db.SaveChangesAsync(ct);
        return user;
    }

    private void AppendSessionCookie(HttpContext ctx, string sessionId) =>
        ctx.Response.Cookies.Append(_session.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax, // Lax survives the top-level redirect back from Keycloak; Strict drops it
            IsEssential = true,
            Path = "/",
            MaxAge = _session.AbsoluteTtl,
            // No Domain → host-only: a session for one host can never be replayed at another (realm isolation).
        });

    private void ClearSessionCookie(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(_session.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    // The public origin is always HTTPS here (TLS terminates at Cloudflare; Envoy overwrites the
    // internal :scheme to http). Build redirect/post-logout URLs from the preserved Host.
    private static string Origin(HttpContext ctx) => $"https://{ctx.Request.Host.Value}";

    private static string SafeReturnUrl(string? raw) =>
        !string.IsNullOrEmpty(raw) && raw.StartsWith('/') && !raw.StartsWith("//") ? raw : "/app";
}
