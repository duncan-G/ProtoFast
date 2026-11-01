namespace ProtoFast.Auth.Api;

/// <summary>
/// Identity headers the edge's authorizer answers (<see cref="Endpoints.ExtAuthzEndpoint"/>) for
/// the edge to add to a request. The edge removes the client's own copies of every one of them
/// first, which is why the spec's <c>authorize.headers</c> lists them all. Backends trust only
/// <see cref="InternalJwt"/>; the others are conveniences for SSR personalization.
/// </summary>
public static class AuthHeaders
{
    public const string UserId = "x-user-id";
    public const string Tenant = "x-tenant";
    public const string Roles = "x-roles";
    public const string InternalJwt = "x-internal-jwt";
    public const string Authenticated = "x-authenticated";

    /// <summary>Every header the authorizer answers, as the spec's <c>authorize.headers</c>
    /// lists them.</summary>
    public static readonly string[] All = [UserId, Tenant, Roles, InternalJwt, Authenticated];
}
