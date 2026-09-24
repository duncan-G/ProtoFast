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
        //
        // A '+' in a configured key encodes "host:port" ("localhost+20002" → localhost:20002).
        // It cannot be written with a colon: .NET configuration treats ':' in a key as a path
        // separator, so "localhost:20002" silently flattens into nested keys and never binds.
        // '+' can never appear in a real host, so the translation is unambiguous.
        var byHost = new Dictionary<string, TenantConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var (host, tenant) in options.Value.ByHost)
        {
            byHost[host.Replace('+', ':')] = tenant;
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

        // A "host:port" entry wins over the bare host. In dev every client shares localhost and
        // the per-client Envoy listeners differ only by port, so the port is the only thing that
        // can put two listeners in two realms (e.g. localhost:20002 → theplot while localhost
        // stays protofast). Production hosts are distinct names and never need port entries.
        return _byHost.TryGetValue(Normalize(host, keepPort: true), out tenant)
            || _byHost.TryGetValue(Normalize(host), out tenant);
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

    private static string Normalize(string host, bool keepPort = false)
    {
        var span = host.AsSpan().Trim();

        var colon = span.IndexOf(':');
        if (colon >= 0 && !keepPort)
        {
            // Drop an optional port (Host or :authority can carry one).
            span = span[..colon];
        }

        if (colon >= 0 && keepPort)
        {
            var name = span[..colon].TrimEnd('.');
            return string.Concat(name, span[colon..]).ToLowerInvariant();
        }

        return span.TrimEnd('.').ToString().ToLowerInvariant();
    }
}
