using System.Net;

namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>The token set from a Keycloak <c>/token</c> exchange or refresh, with expiries
/// resolved to absolute instants.</summary>
public sealed record KeycloakTokens(
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt);

/// <summary>Raised when a Keycloak back-channel call fails (non-2xx token endpoint, etc.).</summary>
public sealed class KeycloakException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    /// <summary>The token-endpoint status, when the failure came from a Keycloak response rather
    /// than a transport error.</summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>A 4xx is Keycloak rejecting the grant itself — a revoked or already-ended session
    /// will never come back, so retrying is pointless. 5xx and transport failures are transient.</summary>
    public bool IsGrantRejected =>
        StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}
