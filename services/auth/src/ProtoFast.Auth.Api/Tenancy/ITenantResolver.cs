using System.Diagnostics.CodeAnalysis;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Tenancy;

/// <summary>Resolves the tenant (realm + client) from the request <c>Host</c>/<c>:authority</c>.
/// An unmapped host returns false — never guess a realm (guide §3.3).</summary>
public interface ITenantResolver
{
    bool TryResolve(string? host, [NotNullWhen(true)] out TenantConfig? tenant);
}
