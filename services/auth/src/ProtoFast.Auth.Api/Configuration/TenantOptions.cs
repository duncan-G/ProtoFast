namespace ProtoFast.Auth.Api.Configuration;

/// <summary>
/// Host → realm/client map. Config now, a DB row later (architecture doc). Adding a tenant is
/// one entry, no code change; a <c>Host</c> not in the map is never guessed — it routes public.
/// </summary>
public sealed class TenantOptions
{
    public Dictionary<string, TenantConfig> ByHost { get; init; } = new();
}

public sealed class TenantConfig
{
    public string Realm { get; init; } = "";
    public string ClientId { get; init; } = "";
}
