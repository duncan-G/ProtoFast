namespace ProtoFast.Auth.Api.Configuration;

/// <summary>
/// The SMTP relay auth-svc sends its own mail through — today, the email-change code and the
/// heads-up that follows it.
///
/// <para>Bound from the <c>Smtp</c> section, which in a deployment arrives as the
/// <c>Auth_Smtp__*</c> environment variables. Those are the same Secrets Manager entries
/// <c>deploy.sh</c> already resolves for Keycloak's own <c>SMTP_*</c> — one relay and one set of
/// credentials, with two senders on it, rather than a second mail identity to verify.</para>
/// </summary>
public sealed class SmtpOptions
{
    public string Host { get; init; } = "";

    public int Port { get; init; } = 587;

    /// <summary>The envelope sender. Must be an address the relay is allowed to send as (on SES,
    /// a verified identity) or the relay rejects the message.</summary>
    public string From { get; init; } = "no-reply@protofast.dev";

    public string FromDisplayName { get; init; } = "Protofast";

    /// <summary>STARTTLS on the submission port. Off only for a local mail catcher.</summary>
    public bool StartTls { get; init; } = true;

    public string User { get; init; } = "";

    public string Password { get; init; } = "";

    /// <summary>
    /// Whether mail can be sent at all. An unconfigured relay is not a startup failure — sign-in
    /// does not need it, Keycloak sends its own mail through its own config — but it does mean the
    /// endpoints that mail a code have to say so rather than silently doing nothing.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
