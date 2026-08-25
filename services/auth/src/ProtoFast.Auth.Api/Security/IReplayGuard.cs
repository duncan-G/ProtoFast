namespace ProtoFast.Auth.Api.Security;

/// <summary>
/// One-shot claim on a key. The first caller within the window wins; every later one is refused.
/// Backs <c>jti</c> replay detection on the back-channel logout endpoint.
/// </summary>
public interface IReplayGuard
{
    /// <summary>True when this caller claimed <paramref name="key"/>, false when someone already
    /// had it.</summary>
    Task<bool> TryClaimAsync(string key, TimeSpan window, CancellationToken ct = default);

    /// <summary>Gives a claim back, so the work it guarded can be attempted again. For the caller
    /// that claimed the key and then failed: a claim held over work that never happened reads to
    /// every later caller as work already done.</summary>
    Task ReleaseAsync(string key, CancellationToken ct = default);
}
