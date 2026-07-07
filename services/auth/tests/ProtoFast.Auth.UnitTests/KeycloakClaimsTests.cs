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
