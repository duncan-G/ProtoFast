using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>The identity distilled from a Keycloak token set — what we provision and put in the session.</summary>
/// <param name="SessionId">Keycloak's <c>sid</c>, the realm SSO session. Null when the token set
/// carries none, which leaves the session out of the back-channel logout index.</param>
public sealed record KeycloakIdentity(
    string Subject,
    string Email,
    IReadOnlyList<string> Roles,
    string? SessionId);

/// <summary>
/// Reads identity claims out of freshly issued Keycloak tokens. The access token came straight
/// from the back-channel exchange, so this only decodes it; signature validation on the resolve
/// path lives in <c>Check</c> (guide §3.7).
/// </summary>
public static class KeycloakClaims
{
    private static readonly JwtSecurityTokenHandler Handler = new();

    public static KeycloakIdentity Read(string accessToken, string? idToken)
    {
        var token = Handler.ReadJwtToken(accessToken);

        var subject = token.Subject
                      ?? token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                      ?? "";

        var email = Claim(token, "email")
                    ?? ClaimFromIdToken(idToken, "email")
                    ?? "";

        // The id token always carries `sid`; the access token only does on newer Keycloak, so
        // read it there first and fall back.
        var sessionId = ClaimFromIdToken(idToken, "sid") ?? Claim(token, "sid");

        return new KeycloakIdentity(subject, email, ReadRealmRoles(token), sessionId);
    }

    private static string? Claim(JwtSecurityToken token, string type) =>
        token.Claims.FirstOrDefault(c => c.Type == type)?.Value;

    private static string? ClaimFromIdToken(string? idToken, string type) =>
        string.IsNullOrEmpty(idToken) ? null : Claim(Handler.ReadJwtToken(idToken), type);

    private static IReadOnlyList<string> ReadRealmRoles(JwtSecurityToken token)
    {
        // Keycloak encodes realm roles as a nested JSON object claim: realm_access = { "roles": [...] }.
        var realmAccess = token.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
        if (string.IsNullOrEmpty(realmAccess))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                return roles.EnumerateArray()
                    .Select(r => r.GetString())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Select(r => r!)
                    .ToArray();
            }
        }
        catch (JsonException)
        {
            // Malformed claim — treat as no roles rather than failing the sign-in.
        }

        return [];
    }
}
