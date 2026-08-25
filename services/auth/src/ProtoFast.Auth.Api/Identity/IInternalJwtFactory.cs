namespace ProtoFast.Auth.Api.Identity;

/// <summary>A minted internal JWT and its absolute expiry — cached in the session, not minted
/// per request (guide §3.9).</summary>
public sealed record InternalJwt(string Token, DateTimeOffset ExpiresAt);

/// <summary>Mints the ES256-signed internal JWT (<c>sub</c>/<c>tenant</c>/<c>roles</c>/
/// <c>subscribed</c>) that backends trust.</summary>
public interface IInternalJwtFactory
{
    /// <param name="subscribed">Whether the account has a live subscription. Minted here rather
    /// than read from a Keycloak claim: this service already knows it from the local user row,
    /// and putting it in Keycloak would mean an admin credential to write it and staleness
    /// between token refreshes.</param>
    InternalJwt Create(string subject, string tenant, IReadOnlyList<string> roles, bool subscribed = false);
}
