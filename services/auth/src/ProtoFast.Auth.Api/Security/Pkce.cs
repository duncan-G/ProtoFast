using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ProtoFast.Auth.Api.Security;

/// <summary>PKCE (RFC 7636) with the S256 method — a fresh verifier + its SHA-256 challenge.</summary>
public static class Pkce
{
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }
}
