namespace ProtoFast.ServiceDefaults.Secrets;

public sealed class SecretsManagerOptions
{
    public string SecretId { get; set; } = null!;

    public string? Prefix { get; set; }

    public TimeSpan? ReloadAfter { get; set; }

    /// <summary>
    /// Region the secret lives in. Leave unset to use the SDK's normal resolution
    /// (AWS_REGION / AWS_DEFAULT_REGION, then the active profile's region).
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Shared-config profile to authenticate with. Leave unset to use AWS_PROFILE,
    /// the default profile, or the instance role in production.
    /// </summary>
    public string? Profile { get; set; }
}
