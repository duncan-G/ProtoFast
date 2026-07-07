namespace ProtoFast.Auth.Api.Correlation;

/// <summary>
/// The short-lived per-authorize state, keyed by the OIDC <c>state</c> value: the PKCE verifier,
/// the exact redirect URI used, where to send the user afterwards, and the resolved tenant. Held
/// server-side (Redis) and consumed once on callback — this is the CSRF guard (guide §3.5).
/// </summary>
/// <param name="Traceparent">W3C traceparent of the <c>/signin</c> request, carried across the
/// Keycloak round-trip so the callback resumes the same trace. Empty for entries written before
/// this field existed.</param>
public sealed record CorrelationData(
    string CodeVerifier,
    string RedirectUri,
    string ReturnUrl,
    string Realm,
    string ClientId,
    string Traceparent = "");
