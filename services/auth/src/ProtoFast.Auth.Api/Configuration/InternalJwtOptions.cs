namespace ProtoFast.Auth.Api.Configuration;

/// <summary>
/// Internal-JWT signing. ES256 asymmetric: auth-svc holds the EC P-256 PRIVATE key
/// and signs; backends hold only the PUBLIC key and verify — a compromised api/payments cannot
/// forge identity. The <see cref="KeyId"/> (<c>kid</c>) supports rotation.
/// </summary>
public sealed class InternalJwtOptions
{
    public string PrivateKeyPem { get; init; } = "";

    /// <summary>Path to a file holding the PEM. Takes precedence over <see cref="PrivateKeyPem"/>
    /// when set — prod mounts the key as a root-only file to avoid newline-in-env-var issues.</summary>
    public string PrivateKeyPemFile { get; init; } = "";

    public string KeyId { get; init; } = "";
    public string Issuer { get; init; } = "protofast-auth";
    public string Audience { get; init; } = "protofast-internal";
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(5);
}
