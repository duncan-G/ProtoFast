namespace ProtoFast.Auth.Api.Identity;

/// <summary>A minted internal JWT and its absolute expiry — cached in the session, not minted
/// per request (guide §3.9).</summary>
public sealed record InternalJwt(string Token, DateTimeOffset ExpiresAt);

/// <summary>Mints the ES256-signed internal JWT (<c>sub</c>/<c>tenant</c>/<c>roles</c>) that
/// backends trust.</summary>
public interface IInternalJwtFactory
{
    InternalJwt Create(string subject, string tenant, IReadOnlyList<string> roles);
}
