namespace ProtoFast.Auth.Api.Endpoints;

/// <summary>
/// Account management (<c>/account/*</c>) — the endpoints behind the app's account page. Plain
/// HTTP like the OIDC endpoints next door, and routed by Envoy with ext_authz OFF for the same
/// reason: auth-svc owns sessions, so it authenticates these itself rather than trusting headers
/// it would otherwise be the source of.
///
/// <para>Envoy prefixes <c>/account/</c> to this cluster (see
/// <c>proxy/envoy.vhost.yaml.tmpl</c>). Keep new endpoints under that prefix or they are served
/// by the SPA.</para>
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account");

        // The account as the page renders it: email, and the passkeys that reach it.
        account.MapGet("/me", (HttpContext ctx, AccountFlow flow, CancellationToken ct) =>
            flow.GetAsync(ctx, ct));

        // Change the email address, in two steps and without leaving the app: ask for a code,
        // then send it back. Keycloak's account console is never linked to — auth-svc does the
        // verifying and writes the address itself once the code checks out.
        account.MapPost("/email", (HttpContext ctx, AccountFlow flow, CancellationToken ct) =>
            flow.RequestEmailChangeAsync(ctx, ct));
        account.MapPost("/email/confirm", (HttpContext ctx, AccountFlow flow, CancellationToken ct) =>
            flow.ConfirmEmailChangeAsync(ctx, ct));
        account.MapDelete("/email", (HttpContext ctx, AccountFlow flow, CancellationToken ct) =>
            flow.CancelEmailChangeAsync(ctx, ct));

        // Remove one passkey. Adding one is /add-passkey, over in AuthEndpoints: enrolment is a
        // Keycloak ceremony that needs the browser, removal is a single Admin API call.
        account.MapDelete("/passkeys/{credentialId}", (
            HttpContext ctx, AccountFlow flow, string credentialId, CancellationToken ct) =>
            flow.DeletePasskeyAsync(ctx, credentialId, ct));

        // Delete the account outright.
        account.MapPost("/delete", (HttpContext ctx, AccountFlow flow, CancellationToken ct) =>
            flow.DeleteAccountAsync(ctx, ct));

        return app;
    }
}
