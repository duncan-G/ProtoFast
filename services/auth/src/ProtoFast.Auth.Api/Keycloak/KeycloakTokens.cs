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
public sealed class KeycloakException(string message) : Exception(message);
