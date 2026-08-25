using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Tenancy;

public sealed class TenantResolver : ITenantResolver
{
    private readonly IReadOnlyDictionary<string, TenantConfig> _byHost;
    private readonly IReadOnlyDictionary<(string Realm, string ClientId), TenantConfig> _byClient;
    private readonly IReadOnlySet<string> _realms;

    public TenantResolver(IOptions<TenantOptions> options)
    {
        // Host comparison is case-insensitive; a stray port or trailing dot shouldn't matter.
        _byHost = new Dictionary<string, TenantConfig>(options.Value.ByHost, StringComparer.OrdinalIgnoreCase);

        // Several hosts may share a realm/client pair; the first wins, since callers only need the
        // realm and client back out and every entry for a pair carries the same two.
        var byClient = new Dictionary<(string, string), TenantConfig>();
        foreach (var tenant in _byHost.Values)
        {
            byClient.TryAdd((tenant.Realm, tenant.ClientId), tenant);
        }

        _byClient = byClient;
        _realms = byClient.Keys.Select(k => k.Item1).ToHashSet(StringComparer.Ordinal);
    }

    public bool TryResolve(string? host, [NotNullWhen(true)] out TenantConfig? tenant)
    {
        tenant = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalized = Normalize(host);
        return _byHost.TryGetValue(normalized, out tenant);
    }

    public bool TryResolveByClient(string? realm, string? clientId, [NotNullWhen(true)] out TenantConfig? tenant)
    {
        tenant = null;
        if (string.IsNullOrWhiteSpace(realm) || string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        return _byClient.TryGetValue((realm, clientId), out tenant);
    }

    public bool KnowsRealm(string? realm) =>
        !string.IsNullOrWhiteSpace(realm) && _realms.Contains(realm);

    private static string Normalize(string host)
    {
        var span = host.AsSpan().Trim();

        // Drop an optional port (Host or :authority can carry one).
        var colon = span.IndexOf(':');
        if (colon >= 0)
        {
            span = span[..colon];
        }

        return span.TrimEnd('.').ToString().ToLowerInvariant();
    }
}
