using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ProtoFast.ServiceDefaults.InternalAuth;

/// <summary>
/// Validates the ES256 internal JWT with the public key only. Returns a <see cref="ClaimsPrincipal"/>
/// for a valid token or null for anything else (no key configured, missing/expired/forged token).
/// </summary>
public sealed class InternalJwtValidator : IDisposable
{
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly TokenValidationParameters? _parameters;
    private readonly ECDsa? _ecdsa;

    public InternalJwtValidator(IOptions<InternalJwtValidationOptions> options)
    {
        var o = options.Value;
        var pem = !string.IsNullOrWhiteSpace(o.PublicKeyPemFile)
            ? File.ReadAllText(o.PublicKeyPemFile)
            : o.PublicKeyPem;

        if (string.IsNullOrWhiteSpace(pem))
        {
            return; // not configured → IsConfigured false → everything fails closed
        }

        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(pem);

        var key = new ECDsaSecurityKey(_ecdsa);
        if (!string.IsNullOrEmpty(o.KeyId))
        {
            key.KeyId = o.KeyId;
        }

        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = o.Issuer,
            ValidateAudience = true,
            ValidAudience = o.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256], // pin ES256; never accept alg downgrade
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "roles",
        };
    }

    /// <summary>True once a public key is loaded. When false, every token is rejected.</summary>
    public bool IsConfigured => _parameters is not null;

    public ClaimsPrincipal? Validate(string? token)
    {
        if (_parameters is null || string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            return _handler.ValidateToken(token, _parameters, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    public void Dispose() => _ecdsa?.Dispose();
}
