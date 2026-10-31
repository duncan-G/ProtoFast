using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Api.Tenancy;

namespace ProtoFast.Auth.Api.Endpoints;

/// <summary>
/// The edge's authorizer. ProtoStrap's edge asks auth about every request to the clients, the
/// api and payments (their spec's <c>authorize</c>) before sending it on: any method, at
/// <c>/ext-authz</c> followed by the request's own path and query, with its Host and cookie.
/// The edge has already removed the client's own copies of the identity headers, adds the ones
/// answered here to the request, and passes <c>set-cookie</c> back to the browser.
///
/// <para>Annotate-only, like the gRPC Check it replaces: every answer is 200, carrying the
/// identity for a live session and <c>x-authenticated: false</c> otherwise. Enforcement stays
/// downstream, in SSR's redirect for pages and the internal JWT for the gRPC services. The one
/// exception is an app console on the admin host (docs/design/admin-consoles.md): only holders
/// of its role reach its process, so anyone else gets a redirect, which the edge gives the
/// browser as it is. This path is never routed: only the edge calls it, on the overlay.</para>
/// </summary>
public static class ExtAuthzEndpoint
{
    public const string Prefix = "/ext-authz";

    /// <summary>What auth answers: a status, a redirect's location, and headers.</summary>
    public sealed record Answer(int Status, string? Location, IReadOnlyList<KeyValuePair<string, string>> Headers);

    public static IEndpointRouteBuilder MapExtAuthz(this IEndpointRouteBuilder app)
    {
        app.Map(Prefix + "/{**path}", CheckAsync);
        return app;
    }

    private static async Task CheckAsync(
        HttpContext http,
        SessionResolver resolver,
        ITenantResolver tenants,
        IOptions<SessionPolicyOptions> session,
        ILoggerFactory loggers)
    {
        var path = "/" + (http.Request.RouteValues["path"] as string ?? "");
        var host = http.Request.Host.Value;
        tenants.TryResolve(host, out var tenant);

        ResolvedIdentity? identity = null;
        try
        {
            identity = await resolver.ResolveAsync(http.Request.Headers.Cookie.ToString(), host, http.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Annotate-only: a resolver failure degrades to anonymous, never a deny.
            loggers.CreateLogger(typeof(ExtAuthzEndpoint)).LogError(ex, "Session resolution failed; treating request as anonymous");
        }

        var policy = session.Value;
        var answer = Decide(tenant, path, path + http.Request.QueryString, identity, id => SessionCookieHeader(policy, id));
        foreach (var (name, value) in answer.Headers)
        {
            http.Response.Headers.Append(name, value);
        }

        if (answer.Location is not null)
        {
            http.Response.Headers.Location = answer.Location;
        }

        http.Response.StatusCode = answer.Status;
    }

    /// <summary>
    /// Decide answers for a request to <paramref name="path"/> (with its query,
    /// <paramref name="pathAndQuery"/>) on a host of <paramref name="tenant"/>, null for an
    /// unknown host. <paramref name="cookie"/> writes the Set-Cookie of a rotated session id.
    /// </summary>
    public static Answer Decide(
        TenantConfig? tenant, string path, string pathAndQuery, ResolvedIdentity? identity, Func<string, string> cookie)
    {
        var headers = new List<KeyValuePair<string, string>>();
        if (identity?.RotatedSessionId is { } rotated)
        {
            // Whatever the answer, the browser must swap to the new id, or the old one lapses.
            headers.Add(new("set-cookie", cookie(rotated)));
        }

        if (tenant?.ConsoleRole(path) is { } role)
        {
            if (identity is null)
            {
                return new Answer(StatusCodes.Status302Found, "/signin?returnUrl=" + Uri.EscapeDataString(pathAndQuery), headers);
            }

            if (!identity.Roles.Contains(role, StringComparer.Ordinal))
            {
                return new Answer(StatusCodes.Status302Found, "/forbidden", headers);
            }
        }

        if (identity is null)
        {
            headers.Add(new(AuthHeaders.Authenticated, "false"));
            return new Answer(StatusCodes.Status200OK, null, headers);
        }

        headers.Add(new(AuthHeaders.UserId, identity.Subject));
        headers.Add(new(AuthHeaders.Tenant, identity.Tenant));
        headers.Add(new(AuthHeaders.Roles, string.Join(',', identity.Roles)));
        headers.Add(new(AuthHeaders.InternalJwt, identity.InternalJwt));
        headers.Add(new(AuthHeaders.Authenticated, "true"));
        return new Answer(StatusCodes.Status200OK, null, headers);
    }

    // The same attributes SessionCookie.Append writes, as a header the edge hands the browser.
    private static string SessionCookieHeader(SessionPolicyOptions policy, string sessionId) =>
        $"{policy.CookieName}={sessionId}; Path=/; Secure; HttpOnly; SameSite=Lax; Max-Age={(long)policy.AbsoluteTtl.TotalSeconds}";
}
