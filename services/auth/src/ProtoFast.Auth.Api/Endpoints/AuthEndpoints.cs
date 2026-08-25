namespace ProtoFast.Auth.Api.Endpoints;

/// <summary>
/// The browser-facing OIDC endpoints (guide §3.6). Plain HTTP — they 302 and Set-Cookie, they are
/// not gRPC. Envoy routes these to the auth cluster with ext_authz OFF (they <em>are</em> the flow).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Sign in — Authorization Code + PKCE against the host's realm.
        app.MapGet("/signin", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.StartAsync(ctx, registration: false, ct));

        // Sign up — same flow with prompt=create (Keycloak registration page).
        app.MapGet("/signup", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.StartAsync(ctx, registration: true, ct));

        // Reset — Keycloak's login page carries the "Forgot password?" entry into reset-credentials,
        // then continues the same auth flow back to /signin-oidc.
        app.MapGet("/reset", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.StartAsync(ctx, registration: false, ct, skipIfAuthenticated: false));

        // OIDC callback — code → tokens → provision → session cookie.
        app.MapGet("/signin-oidc", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.CallbackAsync(ctx, ct));

        // Sign out — drop the session, clear the cookie, end the Keycloak SSO session.
        app.MapGet("/signout", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.SignOutAsync(ctx, ct));

        // Back-channel logout — Keycloak POSTs a signed logout token when a realm SSO session
        // ends, which is what carries a sign-out on one host across to the other. Called
        // server-to-server on the internal network; the Envoy vhost never routes it from the edge.
        app.MapPost("/backchannel-logout", (HttpContext ctx, BackchannelLogout logout, CancellationToken ct) =>
            logout.HandleAsync(ctx, ct));

        return app;
    }
}
