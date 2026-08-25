using ProtoFast.Auth.Api.Keycloak;

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

        // Sign up — same flow with prompt=create (Keycloak registration page), carrying the
        // passkey offer on the very same authorize request. Sign-in can afford to chain the
        // offer onto a second round trip because the SSO cookie it just set satisfies it
        // silently; registration cannot. Registration is a different top-level flow, so it
        // records no authenticated level against the browser flow, and Keycloak's Cookie
        // authenticator answers the follow-up authorize with "strong authentication required"
        // and drops the brand-new account back into the sign-in branch — a second mailed code,
        // seconds after the one that just verified the address.
        app.MapGet("/signup", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.StartAsync(ctx, registration: true, ct, kcAction: KeycloakActions.RegisterPasskey));

        // Reset — a forced trip back through Keycloak's credential prompt. The realm has no
        // passwords and no reset-credentials entry any more, so this is now simply "prove who you
        // are again", which is why it still opts out of the already-signed-in short-circuit.
        app.MapGet("/reset", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.StartAsync(ctx, registration: false, ct, skipIfAuthenticated: false));

        // Add a passkey — the one route by which a passkey is ever enrolled. Keycloak performs
        // the ceremony; this only asks for it. skipIfAuthenticated is off deliberately: the
        // caller is signed in by definition, and the default short-circuit would bounce them
        // straight back to the app without ever reaching Keycloak.
        app.MapGet("/add-passkey", (HttpContext ctx, AuthFlow flow, CancellationToken ct) =>
            flow.StartAsync(ctx, registration: false, ct, skipIfAuthenticated: false,
                kcAction: KeycloakActions.RegisterPasskey));

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
