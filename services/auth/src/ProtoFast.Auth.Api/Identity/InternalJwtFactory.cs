using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Identity;

/// <summary>
/// Signs the internal JWT with the EC P-256 private key (ES256). The key and signing credentials
/// are imported once and reused; the token-handler's cached signature provider is thread-safe, so
/// a single singleton serves all requests.
/// </summary>
public sealed class InternalJwtFactory : IInternalJwtFactory, IDisposable
{
    private readonly InternalJwtOptions _options;
    private readonly TimeProvider _clock;
    private readonly ECDsa _ecdsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    public InternalJwtFactory(IOptions<InternalJwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;

        var pem = !string.IsNullOrWhiteSpace(_options.PrivateKeyPemFile)
            ? File.ReadAllText(_options.PrivateKeyPemFile)
            : _options.PrivateKeyPem;

        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(pem);

        var key = new ECDsaSecurityKey(_ecdsa) { KeyId = _options.KeyId };
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
    }

    public InternalJwt Create(string subject, string tenant, IReadOnlyList<string> roles)
    {
        var now = _clock.GetUtcNow();
        var expires = now + _options.Lifetime;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new("tenant", tenant),
        };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new InternalJwt(_handler.WriteToken(token), expires);
    }

    public void Dispose() => _ecdsa.Dispose();
}
