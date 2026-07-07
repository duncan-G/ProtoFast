using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Security;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

public class PkceTests
{
    [Fact]
    public void Challenge_is_the_s256_of_the_verifier()
    {
        var (verifier, challenge) = Pkce.Create();

        var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Assert.Equal(expected, challenge);
    }

    [Fact]
    public void Verifier_is_url_safe()
    {
        var (verifier, _) = Pkce.Create();
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.DoesNotContain('=', verifier);
    }

    [Fact]
    public void Each_call_is_fresh()
    {
        var (v1, _) = Pkce.Create();
        var (v2, _) = Pkce.Create();
        Assert.NotEqual(v1, v2);
    }
}
