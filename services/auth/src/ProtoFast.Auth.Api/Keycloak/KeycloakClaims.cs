using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>The identity distilled from a Keycloak token set — what we provision and put in the session.</summary>
/// <param name="SessionId">Keycloak's <c>sid</c>, the realm SSO session. Null when the token set
/// carries none, which leaves the session out of the back-channel logout index.</param>
/// <param name="Acr">The authentication context class the token was actually issued at. Requesting
/// one through <c>acr_values</c> is a request, not a guarantee, so anything that gates on a level
/// has to read this rather than assume it.</param>
/// <param name="AuthenticationMethods">The <c>amr</c> claim: how the user proved who they are on
/// this authentication. Often absent — Keycloak only emits it in some configurations — so treat an
/// empty list as "not stated", never as "no passkey".</param>
public sealed record KeycloakIdentity(
    string Subject,
    string Email,
    IReadOnlyList<string> Roles,
    string? SessionId,
    string? Acr = null,
    IReadOnlyList<string>? AuthenticationMethods = null)
{
    /// <summary>
    /// Did this sign-in demonstrably use a passkey? True only on a positive statement, which is
    /// what makes it safe to use to repair a stale "has a passkey" flag: a user who authenticated
    /// with one obviously has one, and silence proves nothing either way.
    /// </summary>
    public bool AuthenticatedWithPasskey =>
        AuthenticationMethods is not null
        && AuthenticationMethods.Any(m =>
            m.Equals("webauthn-passwordless", StringComparison.OrdinalIgnoreCase)
            || m.Equals("webauthn", StringComparison.OrdinalIgnoreCase));
}

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

        var acr = ClaimFromIdToken(idToken, "acr") ?? Claim(token, "acr");

        return new KeycloakIdentity(
            subject,
            email,
            ReadRealmRoles(token),
            sessionId,
            acr,
            ReadAuthenticationMethods(token, idToken));
    }

    /// <summary>
    /// The <c>amr</c> claim, which Keycloak encodes as a JSON array. It appears on the id token
    /// when it appears at all, so read there first.
    /// </summary>
    private static IReadOnlyList<string> ReadAuthenticationMethods(JwtSecurityToken token, string? idToken)
    {
        var raw = ClaimFromIdToken(idToken, "amr") ?? Claim(token, "amr");
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        // A single value arrives as a bare string rather than an array.
        if (!raw.StartsWith('['))
        {
            return [raw];
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!)
                    .ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
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
