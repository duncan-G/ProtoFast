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
    /// <summary>
    /// The host this entry matches, for a host the dictionary key cannot spell. Configuration
    /// keys are colon-delimited, so a key carrying a port — <c>"localhost:20002"</c> — is read as
    /// a nested section (<c>localhost</c> → <c>20002</c>) rather than a literal key, and the entry
    /// silently disappears at bind time instead of failing loudly. Where that applies, make the
    /// key a plain label and put the real host here. Empty means the key IS the host, which is
    /// what every port-less production entry uses.
    /// </summary>
    public string Host { get; init; } = "";

    public string Realm { get; init; } = "";
    public string ClientId { get; init; } = "";

    /// <summary>
    /// OIDC <c>max_age</c> (seconds) for this host's authorize requests. Set on the admin
    /// host and nowhere else: without it, a realm SSO session opened on the product site
    /// carries straight into the admin console, silently, for as long as the session lives.
    /// Null leaves silent SSO alone, which is what every other host wants.
    /// </summary>
    public int? MaxAge { get; init; }

    /// <summary>
    /// OIDC <c>acr_values</c> for this host — the ACR name the realm maps to a level of
    /// authentication (<c>acr.loa.map</c>). Asking is not getting: <c>acr_values</c> is
    /// voluntary, so the callback verifies the value that came back and refuses the
    /// sign-in if it did not.
    /// </summary>
    public string? AcrValues { get; init; }
}
