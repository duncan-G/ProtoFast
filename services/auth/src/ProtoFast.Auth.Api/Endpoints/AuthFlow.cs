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
    AuthDbContext db,
    IOptions<SessionPolicyOptions> sessionOptions,
    TimeProvider clock,
    ILogger<AuthFlow> logger)
{
    private readonly SessionPolicyOptions _session = sessionOptions.Value;

    /// <summary>/signin, /signup (registration), /reset — set up correlation, 302 to Keycloak.</summary>
    /// <param name="skipIfAuthenticated">When true (sign-in/sign-up), an already-valid session
    /// short-circuits straight to the return target instead of re-running the flow. Password reset
    /// leaves this off — a signed-in user resetting credentials is a legitimate intent.</param>
    public async Task<IResult> StartAsync(HttpContext ctx, bool registration, CancellationToken ct, bool skipIfAuthenticated = true)
    {
        // These endpoints run with ext_authz OFF, so identity isn't injected — but the session
        // cookie still rides along. If it resolves to a live session the user is already signed in;
        // bounce them to the return target (defaults to /app) rather than looping back to Keycloak.
        if (skipIfAuthenticated)
        {
            var existingSessionId = ctx.Request.Cookies[_session.CookieName];
            if (!string.IsNullOrEmpty(existingSessionId)
                && await sessionStore.GetAsync(existingSessionId, ct) is not null)
            {
                return Results.Redirect(SafeReturnUrl(ctx.Request.Query["returnUrl"]));
            }
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
            new CorrelationData(verifier, redirectUri, returnUrl, tenant.Realm, tenant.ClientId, Activity.Current?.Id ?? ""),
            ct);

        return Results.Redirect(keycloak.BuildAuthorizeUrl(tenant, redirectUri, state, challenge, registration));
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

        await ProvisionAsync(correlation.Realm, identity, ct);

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
        };

        var sessionId = await sessionStore.CreateAsync(session, ct);
        AppendSessionCookie(ctx, sessionId);

        activity?.SetStatus(ActivityStatusCode.Ok);
        return Results.Redirect(correlation.ReturnUrl);
    }

    /// <summary>/signout — drop the session, clear the cookie, 302 to Keycloak end-session.</summary>
    public async Task<IResult> SignOutAsync(HttpContext ctx, CancellationToken ct)
    {
        var sessionId = ctx.Request.Cookies[_session.CookieName];
        string? idTokenHint = null;
        string realm;

        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await sessionStore.GetAsync(sessionId, ct);
            idTokenHint = session?.IdToken;
            realm = session?.Realm ?? RealmFromHost(ctx);
            await sessionStore.DeleteAsync(sessionId, ct);
        }
        else
        {
            realm = RealmFromHost(ctx);
        }

        ClearSessionCookie(ctx);

        if (string.IsNullOrEmpty(realm))
        {
            return Results.Redirect("/");
        }

        return Results.Redirect(keycloak.BuildEndSessionUrl(realm, idTokenHint, Origin(ctx) + "/"));
    }

    private async Task ProvisionAsync(string realm, KeycloakIdentity identity, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Realm == realm && u.Subject == identity.Subject, ct);
        var now = clock.GetUtcNow();

        if (user is null)
        {
            db.Users.Add(new UserAccount
            {
                Id = Guid.NewGuid(),
                Realm = realm,
                Subject = identity.Subject,
                Email = identity.Email,
                CreatedAt = now,
                LastLoginAt = now,
            });
        }
        else
        {
            user.Email = identity.Email;
            user.LastLoginAt = now;
        }

        await db.SaveChangesAsync(ct);
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

    private string RealmFromHost(HttpContext ctx) =>
        tenantResolver.TryResolve(ctx.Request.Host.Value, out var tenant) ? tenant.Realm : "";

    // The public origin is always HTTPS here (TLS terminates at Cloudflare; Envoy overwrites the
    // internal :scheme to http). Build redirect/post-logout URLs from the preserved Host.
    private static string Origin(HttpContext ctx) => $"https://{ctx.Request.Host.Value}";

    private static string SafeReturnUrl(string? raw) =>
        !string.IsNullOrEmpty(raw) && raw.StartsWith('/') && !raw.StartsWith("//") ? raw : "/app";
}
