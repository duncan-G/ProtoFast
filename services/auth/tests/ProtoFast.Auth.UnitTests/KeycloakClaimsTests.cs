using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ProtoFast.Auth.Api.Keycloak;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

public class KeycloakClaimsTests
{
    [Fact]
    public void Reads_subject_email_and_realm_roles()
    {
        var token = BuildToken(
            new Claim("sub", "user-123"),
            new Claim("email", "a@b.com"),
            new Claim("realm_access", """{"roles":["admin","staff"]}""", JsonClaimValueTypes.Json));

        var identity = KeycloakClaims.Read(token, idToken: null);

        Assert.Equal("user-123", identity.Subject);
        Assert.Equal("a@b.com", identity.Email);
        Assert.Equal(["admin", "staff"], identity.Roles);
    }

    [Fact]
    public void Falls_back_to_id_token_for_email()
    {
        var access = BuildToken(new Claim("sub", "user-123"));
        var id = BuildToken(new Claim("sub", "user-123"), new Claim("email", "from-id@b.com"));

        var identity = KeycloakClaims.Read(access, id);

        Assert.Equal("from-id@b.com", identity.Email);
    }

    [Fact]
    public void Reads_the_sso_session_id_preferring_the_id_token()
    {
        // `sid` is what indexes the session for back-channel logout. The id token always carries
        // it; older Keycloak leaves it off the access token.
        var access = BuildToken(new Claim("sub", "user-123"));
        var id = BuildToken(new Claim("sub", "user-123"), new Claim("sid", "sso-1"));

        Assert.Equal("sso-1", KeycloakClaims.Read(access, id).SessionId);
    }

    [Fact]
    public void Falls_back_to_the_access_token_for_the_session_id()
    {
        var access = BuildToken(new Claim("sub", "user-123"), new Claim("sid", "sso-2"));

        Assert.Equal("sso-2", KeycloakClaims.Read(access, idToken: null).SessionId);
    }

    [Fact]
    public void Missing_session_id_is_null_rather_than_empty()
    {
        // Null keeps the session out of the logout index instead of indexing it under "".
        var access = BuildToken(new Claim("sub", "user-123"));

        Assert.Null(KeycloakClaims.Read(access, idToken: null).SessionId);
    }

    [Fact]
    public void Missing_realm_access_yields_no_roles()
    {
        var token = BuildToken(new Claim("sub", "user-123"), new Claim("email", "a@b.com"));

        var identity = KeycloakClaims.Read(token, idToken: null);

        Assert.Empty(identity.Roles);
    }

    private static string BuildToken(params Claim[] claims)
    {
        var jwt = new JwtSecurityToken(claims: claims);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
