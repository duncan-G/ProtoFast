using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>
/// The OIDC adapter for Keycloak (guide §3.5). Browser-facing URLs use the public authority;
/// back-channel calls (token exchange, JWKS) use the internal authority.
/// </summary>
public interface IKeycloakGateway
{
    /// <summary>Authorization-Code-with-PKCE authorize URL. <paramref name="registration"/> adds
    /// <c>prompt=create</c> (the Keycloak registration page).</summary>
    string BuildAuthorizeUrl(TenantConfig tenant, string redirectUri, string state, string codeChallenge, bool registration);

    Task<KeycloakTokens> ExchangeCodeAsync(TenantConfig tenant, string code, string redirectUri, string codeVerifier, CancellationToken ct = default);

    Task<KeycloakTokens> RefreshAsync(TenantConfig tenant, string refreshToken, CancellationToken ct = default);

    /// <summary>RP-initiated logout (end-session) URL for <c>/signout</c>.</summary>
    string BuildEndSessionUrl(string realm, string? idTokenHint, string postLogoutRedirectUri);

    /// <summary>Validation parameters for a realm's access tokens — cached JWKS + issuer
    /// (guide §3.7). Audience is validated separately (Keycloak's <c>azp</c>).</summary>
    Task<TokenValidationParameters> GetValidationParametersAsync(string realm, CancellationToken ct = default);
}
