namespace ProtoFast.Auth.Api.Configuration;

/// <summary>
/// Whether an account has to be subscribed before it reaches the app.
/// </summary>
public sealed class SubscriptionOptions
{
    /// <summary>
    /// Off until billing exists. While it is off, every sign-in takes the subscribed path:
    /// the callback chains straight into the passkey offer and lands the user in the app.
    /// Turning it on makes the callback fork on <c>UserAccount.SubscribedAt</c> and send an
    /// account without one into the checkout workflow instead — which is only a sane place
    /// to send anybody once there is a checkout to send them to.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Query flag appended to the return URL for an account that needs to subscribe. The
    /// Angular app routes on it; the name is shared with
    /// <c>clients/protofast/src/app/subscription/subscription-flag.ts</c>.
    /// </summary>
    public string ReturnUrlFlag { get; init; } = "subscribe";
}
