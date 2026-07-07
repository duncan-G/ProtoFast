namespace ProtoFast.ServiceDefaults.InternalAuth;

/// <summary>
/// How a backend verifies the internal JWT. It holds the EC <b>public</b> key only —
/// never the private key — so a compromised backend can read identity but cannot forge it. Defaults
/// match auth-svc's issuer/audience, so only <see cref="PublicKeyPem"/> must be supplied
/// (<c>Shared_InternalJwt__PublicKeyPem</c>).
/// </summary>
public sealed class InternalJwtValidationOptions
{
    public string PublicKeyPem { get; init; } = "";

    /// <summary>Path to a file holding the PEM. Takes precedence over <see cref="PublicKeyPem"/>
    /// when set — prod mounts the key as a file to avoid newline-in-env-var issues.</summary>
    public string PublicKeyPemFile { get; init; } = "";

    public string KeyId { get; init; } = "";
    public string Issuer { get; init; } = "protofast-auth";
    public string Audience { get; init; } = "protofast-internal";
}
