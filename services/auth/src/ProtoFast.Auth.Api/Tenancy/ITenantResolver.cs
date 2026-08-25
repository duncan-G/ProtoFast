using System.Diagnostics.CodeAnalysis;
using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Tenancy;

/// <summary>Resolves the tenant (realm + client) from the request <c>Host</c>/<c>:authority</c>.
/// An unmapped host returns false — never guess a realm (guide §3.3).</summary>
public interface ITenantResolver
{
    bool TryResolve(string? host, [NotNullWhen(true)] out TenantConfig? tenant);

    /// <summary>The same map read backwards, for requests that arrive with no usable <c>Host</c> —
    /// back-channel logout is Keycloak calling us, so the realm and client come out of the token.
    /// A pair the map doesn't hold is refused rather than trusted.</summary>
    bool TryResolveByClient(string? realm, string? clientId, [NotNullWhen(true)] out TenantConfig? tenant);

    /// <summary>Whether we serve this realm at all, answered without a client. Back-channel logout
    /// reads the realm out of an unverified token to pick the signing keys to verify it with, and
    /// fetching keys is a call to Keycloak — this is the cheap check that keeps an issuer we never
    /// issued against from sending us after them.</summary>
    bool KnowsRealm(string? realm);
}
