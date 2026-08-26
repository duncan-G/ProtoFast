using ProtoFast.Auth.Api.Configuration;

namespace ProtoFast.Auth.Api.Sessions;

/// <summary>
/// The one place the BFF session cookie's attributes are written. Every endpoint that issues or
/// clears the cookie goes through here, because a Delete whose attributes do not match the
/// Append's is not a delete — the browser keeps the original cookie and the "signed out" user
/// stays signed in.
/// </summary>
public static class SessionCookie
{
    public static void Append(HttpContext ctx, SessionPolicyOptions policy, string sessionId) =>
        ctx.Response.Cookies.Append(policy.CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax, // Lax survives the top-level redirect back from Keycloak; Strict drops it
            IsEssential = true,
            Path = "/",
            MaxAge = policy.AbsoluteTtl,
            // No Domain → host-only: a session for one host can never be replayed at another (realm isolation).
        });

    public static void Clear(HttpContext ctx, SessionPolicyOptions policy) =>
        ctx.Response.Cookies.Delete(policy.CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
}
