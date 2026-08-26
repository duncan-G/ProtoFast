using ProtoFast.Auth.Api.Keycloak;

namespace ProtoFast.Auth.Api.Configuration;

public sealed class KeycloakOptions
{
    /// <summary>
    /// Back-channel base used for token exchange and JWKS — the internal cluster URL,
    /// e.g. <c>http://keycloak:8080</c>. The per-realm path is built from the tenant.
    /// </summary>
    public string Authority { get; init; } = "";

    /// <summary>
    /// Browser-facing base for redirects (authorize, end-session) and the token issuer,
    /// e.g. <c>https://auth.protofast.dev</c>. Falls back to <see cref="Authority"/> when empty
    /// (dev, where the browser and the service reach Keycloak at the same URL).
    /// </summary>
    public string PublicAuthority { get; init; } = "";

    // Confidential-client secrets — auth-svc is the only holder (BFF). From the Auth_ SM secret.
    public string ClientSecretProtofastWeb { get; init; } = "";
    public string ClientSecretAdmin { get; init; } = "";

    /// <summary>
    /// Service-account client used for the Admin API calls account management needs — reading a
    /// user's passkeys, removing one, deleting the account. Its service account holds
    /// <c>view-users</c> and <c>manage-users</c> on <c>realm-management</c> and nothing else, and
    /// no sign-in path touches it (see <see cref="IKeycloakAdmin"/>).
    /// </summary>
    public string AdminClientId { get; init; } = "account-admin";

    /// <summary>
    /// The <see cref="AdminClientId"/> secret. Empty disables account management rather than
    /// failing at startup: a deployment whose realm has no such client yet still signs people in,
    /// and the endpoints that need it answer 503.
    /// </summary>
    public string AdminClientSecret { get; init; } = "";

    public string ResolvePublicAuthority() =>
        string.IsNullOrEmpty(PublicAuthority) ? Authority : PublicAuthority;

    public string GetClientSecret(string clientId) => clientId switch
    {
        "protofast-web" => ClientSecretProtofastWeb,
        "admin" => ClientSecretAdmin,
        _ => throw new InvalidOperationException($"No client secret configured for client '{clientId}'.")
    };
}
