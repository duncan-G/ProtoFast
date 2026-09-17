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
        // Host comparison is case-insensitive and a trailing dot shouldn't matter. The PORT is
        // kept: in dev every client is on localhost and the port is the only thing telling them
        // apart, so dropping it here would hand every host the first client that claimed
        // "localhost". Keys are normalized the same way lookups are, so a map entry may be
        // written with or without a port.
        // An entry's host is TenantConfig.Host when set, otherwise the key itself — see the
        // remarks there for why a key alone cannot carry a port.
        var byHost = new Dictionary<string, TenantConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, tenant) in options.Value.ByHost)
        {
            var host = NormalizeKey(string.IsNullOrWhiteSpace(tenant.Host) ? key : tenant.Host);
            if (!byHost.TryAdd(host, tenant))
            {
                throw new InvalidOperationException(
                    $"Two tenant entries claim the host '{host}'; a host maps to exactly one realm and client.");
            }
        }

        _byHost = byHost;

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

        // Most specific first: an entry naming the port wins, and a portless entry still serves
        // every port on that name. That ordering is what lets one map hold dev's
        // "localhost:20002" beside production's bare "theplot.protofast.dev".
        var normalized = NormalizeKey(host);
        if (_byHost.TryGetValue(normalized, out tenant))
        {
            return true;
        }

        var colon = normalized.IndexOf(':');
        return colon >= 0 && _byHost.TryGetValue(normalized[..colon], out tenant);
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

    /// <summary>Case- and trailing-dot-insensitive, port preserved. A bare IPv6 literal would
    /// need brackets to carry a port at all, so splitting on the last colon is not safe and the
    /// value is left whole — an unbracketed literal simply has to be mapped verbatim.</summary>
    private static string NormalizeKey(string host) =>
        host.Trim().TrimEnd('.').ToLowerInvariant();
}
