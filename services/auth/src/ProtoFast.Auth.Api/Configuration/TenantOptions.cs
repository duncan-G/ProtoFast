namespace ProtoFast.Auth.Api.Configuration;

/// <summary>
/// Host → realm/client map. Config now, a DB row later (architecture doc). Adding a tenant is
/// one entry, no code change; a <c>Host</c> not in the map is never guessed — it routes public.
/// A key is one of the spec's host names (protofast, admin, theplot), whose hostname in this
/// account arrives as HOST_&lt;NAME&gt;, or else a hostname itself.
/// </summary>
public sealed class TenantOptions
{
    public Dictionary<string, TenantConfig> ByHost { get; init; } = new();
}

public sealed class TenantConfig
{
    public string Realm { get; init; } = "";
    public string ClientId { get; init; } = "";

    /// <summary>
    /// OIDC <c>max_age</c> (seconds) for this host's authorize requests. Set on the admin
    /// host and nowhere else: without it, a live operators-realm SSO session carries straight
    /// into the admin console, silently, for as long as the session lives. Null leaves silent
    /// SSO alone, which is what every other host wants.
    /// </summary>
    public int? MaxAge { get; init; }

    /// <summary>
    /// OIDC <c>acr_values</c> for this host — the ACR name the realm maps to a level of
    /// authentication (<c>acr.loa.map</c>). Asking is not getting: <c>acr_values</c> is
    /// voluntary, so the callback verifies the value that came back and refuses the
    /// sign-in if it did not.
    /// </summary>
    public string? AcrValues { get; init; }

    /// <summary>
    /// Realm roles of which the account must hold at least one for this host to issue or keep a
    /// session: the console roles on the admin host (docs/design/admin-consoles.md §4.3). Empty
    /// admits any account in the realm.
    /// </summary>
    public string[] RequiredRoles { get; init; } = [];

    /// <summary>
    /// App consoles mounted on this host, by the first segment of their path, each with the realm
    /// role that reaches it: <c>theplot</c> → <c>admin-theplot</c> serves /theplot/ only to
    /// holders of that role (docs/design/admin-consoles.md). The edge's authorizer enforces it.
    /// </summary>
    public Dictionary<string, string> Consoles { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The role a path's console needs, or null for a path no console holds.</summary>
    public string? ConsoleRole(string path)
    {
        var segment = path.TrimStart('/');
        var slash = segment.IndexOf('/');
        if (slash >= 0)
        {
            segment = segment[..slash];
        }

        return segment.Length > 0 && Consoles.TryGetValue(segment, out var role) ? role : null;
    }

    public bool Admits(IReadOnlyList<string> roles) =>
        RequiredRoles.Length == 0 || RequiredRoles.Any(r => roles.Contains(r, StringComparer.Ordinal));
}
