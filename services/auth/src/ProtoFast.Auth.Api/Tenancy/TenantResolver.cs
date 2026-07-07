using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Tenancy;

public sealed class TenantResolver : ITenantResolver
{
    private readonly IReadOnlyDictionary<string, TenantConfig> _byHost;

    public TenantResolver(IOptions<TenantOptions> options)
    {
        // Host comparison is case-insensitive; a stray port or trailing dot shouldn't matter.
        _byHost = new Dictionary<string, TenantConfig>(options.Value.ByHost, StringComparer.OrdinalIgnoreCase);
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
