namespace ProtoFast.Auth.Api.Configuration;

/// <summary>
/// BFF session-cookie + Redis lifetime policy . Named <c>SessionPolicyOptions</c> to
/// avoid the clash with ASP.NET's <see cref="Microsoft.AspNetCore.Builder.SessionOptions"/>; binds
/// from the <c>Session</c> config section. Secure-leaning defaults; all knobs are config. The
/// session must never outlive Keycloak's ability to refresh (mirror §2.7).
/// </summary>
public sealed class SessionPolicyOptions
{
    public string CookieName { get; init; } = "pf_session";

    /// <summary>Sliding idle window — Redis key TTL, reset on every successful resolve/refresh.</summary>
    public TimeSpan IdleTtl { get; init; } = TimeSpan.FromHours(8);

    /// <summary>Hard cap from <c>createdAt</c> → full re-auth, even if the key is still warm.</summary>
    public TimeSpan AbsoluteTtl { get; init; } = TimeSpan.FromDays(7);

    /// <summary>New opaque id + Set-Cookie on refresh; the old id lapses after a short grace.</summary>
    public bool RotateIdOnRefresh { get; init; } = true;
}
