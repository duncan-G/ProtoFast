namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>
/// Keycloak's Application Initiated Actions — the supported way to run one required
/// action inside an ordinary authorize request, with a cancel path and a reported
/// outcome. Cancelling does not fail the sign-in.
/// </summary>
public static class KeycloakActions
{
    /// <summary>Enrol a passkey. The only action this service ever initiates.</summary>
    public const string RegisterPasskey = "webauthn-register-passwordless";

    /// <summary>
    /// Value of <c>kc_action_parameter</c> that turns the passkey action into a no-op
    /// for a user who already has one. It is the backstop, not the primary check: the
    /// callback skips the round trip entirely when the local record already says the
    /// user has enrolled.
    /// </summary>
    public const string SkipIfExists = "skip_if_exists";

    /// <summary>Query parameter Keycloak puts on the callback saying how the action went.</summary>
    public const string StatusParameter = "kc_action_status";

    /// <summary>The one <see cref="StatusParameter"/> value that means a credential now exists.
    /// The others are <c>cancelled</c> and <c>error</c>, neither of which fails the sign-in.</summary>
    public const string StatusSuccess = "success";
}
