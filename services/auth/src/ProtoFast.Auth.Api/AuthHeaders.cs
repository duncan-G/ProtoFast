namespace ProtoFast.Auth.Api;

/// <summary>
/// Identity headers the ext_authz <c>Check</c> injects on authenticated requests and strips
/// from every inbound request (anti-spoofing). Backends trust only
/// <see cref="InternalJwt"/>; the others are conveniences for SSR personalization.
/// </summary>
public static class AuthHeaders
{
    public const string UserId = "x-user-id";
    public const string Tenant = "x-tenant";
    public const string Roles = "x-roles";
    public const string InternalJwt = "x-internal-jwt";
    public const string Authenticated = "x-authenticated";

    /// <summary>The injected identity headers (everything except the <see cref="Authenticated"/>
    /// flag). On anonymous requests these are removed so a client can't smuggle them; on
    /// authenticated requests they are overwritten with trusted values.</summary>
    public static readonly string[] Identity = [UserId, Tenant, Roles, InternalJwt];
}
