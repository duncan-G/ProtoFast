using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Identity;
using ProtoFast.ServiceDefaults.InternalAuth;
using Xunit;

namespace ProtoFast.Auth.UnitTests;

/// <summary>
/// The cross-service trust contract: auth-svc signs with the EC private key (ES256), backends
/// verify with the public key only. Forged/tampered/expired/wrong-key tokens must not validate.
/// </summary>
public class InternalJwtTrustTests
{
    private const string Issuer = "protofast-auth";
    private const string Audience = "protofast-internal";

    private sealed record KeyPair(string PrivatePem, string PublicPem);

    private static KeyPair NewKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new KeyPair(ec.ExportPkcs8PrivateKeyPem(), ec.ExportSubjectPublicKeyInfoPem());
    }

    private static IInternalJwtFactory Factory(string privatePem, TimeProvider? clock = null) =>
        new InternalJwtFactory(
            Options.Create(new InternalJwtOptions
            {
                PrivateKeyPem = privatePem,
                KeyId = "test-1",
                Issuer = Issuer,
                Audience = Audience,
                Lifetime = TimeSpan.FromMinutes(5),
            }),
            clock ?? TimeProvider.System);

    private static InternalJwtValidator Validator(string publicPem, string? audience = null) =>
        new(Options.Create(new InternalJwtValidationOptions
        {
            PublicKeyPem = publicPem,
            Issuer = Issuer,
            Audience = audience ?? Audience,
        }));

    [Fact]
    public void Valid_token_round_trips_with_identity_claims()
    {
        var keys = NewKeyPair();
        var minted = Factory(keys.PrivatePem).Create("user-123", "protofast", ["admin", "staff"]);

        var principal = Validator(keys.PublicPem).Validate(minted.Token);

        Assert.NotNull(principal);
        Assert.Equal("user-123", principal!.FindFirst("sub")?.Value);
        Assert.Equal("protofast", principal.FindFirst("tenant")?.Value);
        Assert.Equal(["admin", "staff"], principal.FindAll("roles").Select(c => c.Value));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var keys = NewKeyPair();
        var minted = Factory(keys.PrivatePem).Create("user-123", "protofast", []);

        Assert.Null(Validator(keys.PublicPem).Validate(minted.Token + "x"));
    }

    [Fact]
    public void Token_signed_by_a_different_key_is_rejected()
    {
        var signer = NewKeyPair();
        var otherVerifier = NewKeyPair();
        var minted = Factory(signer.PrivatePem).Create("user-123", "protofast", []);

        // A compromised backend with the wrong public key cannot accept forged identity.
        Assert.Null(Validator(otherVerifier.PublicPem).Validate(minted.Token));
    }

    [Fact]
    public void Wrong_audience_is_rejected()
    {
        var keys = NewKeyPair();
        var minted = Factory(keys.PrivatePem).Create("user-123", "protofast", []);

        Assert.Null(Validator(keys.PublicPem, audience: "someone-else").Validate(minted.Token));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var keys = NewKeyPair();
        // Mint as if 10 minutes ago, so the 5-minute token is already past expiry + clock skew.
        var pastClock = new FixedClock(DateTimeOffset.UtcNow.AddMinutes(-10));
        var minted = Factory(keys.PrivatePem, pastClock).Create("user-123", "protofast", []);

        Assert.Null(Validator(keys.PublicPem).Validate(minted.Token));
    }

    [Fact]
    public void Validator_without_a_key_is_not_configured_and_rejects_everything()
    {
        var keys = NewKeyPair();
        var minted = Factory(keys.PrivatePem).Create("user-123", "protofast", []);

        var validator = new InternalJwtValidator(Options.Create(new InternalJwtValidationOptions()));

        Assert.False(validator.IsConfigured);
        Assert.Null(validator.Validate(minted.Token));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
