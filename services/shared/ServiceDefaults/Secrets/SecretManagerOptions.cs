namespace ProtoFast.ServiceDefaults.Secrets;

public sealed class SecretsManagerOptions
{
    public string SecretId { get; set; } = null!;

    public string? Prefix { get; set; }

    public TimeSpan? ReloadAfter { get; set; }
}
