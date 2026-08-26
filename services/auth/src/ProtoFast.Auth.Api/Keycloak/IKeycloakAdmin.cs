namespace ProtoFast.Auth.Api.Keycloak;

/// <summary>
/// One of a user's WebAuthn credentials as Keycloak reports it.
/// </summary>
/// <param name="Id">Keycloak's credential id — the handle the delete call takes.</param>
/// <param name="Label">The name the user gave the credential during enrolment, or "" when they
/// gave none. Keycloak does not invent one, so the UI supplies its own fallback.</param>
/// <param name="CreatedAt">When the credential was enrolled, or null when Keycloak reported no
/// creation date (older credentials).</param>
/// <param name="Passwordless">True for a passkey (<c>webauthn-passwordless</c>), false for a
/// second-factor WebAuthn credential (<c>webauthn</c>). The realm only ever enrols the former,
/// but a credential added before that was true would still be listed here.</param>
public sealed record WebAuthnCredential(string Id, string Label, DateTimeOffset? CreatedAt, bool Passwordless);

/// <summary>
/// The narrow slice of Keycloak's Admin API that account management needs: read a user's WebAuthn
/// credentials, delete one, delete the user outright.
///
/// <para>This is the one standing admin credential in the system — a service-account client with
/// <c>view-users</c> and <c>manage-users</c> on <c>realm-management</c>, and nothing else. The
/// sign-in path deliberately does not use it (a passkey's existence is answered by
/// <c>UserAccount.PasskeyRegisteredAt</c> and the <c>amr</c> claim, see the credential plan §2.5):
/// it exists because a user removing a credential or deleting their account are acts nobody else
/// can perform on their behalf, and there is no OIDC flow that expresses either.</para>
/// </summary>
public interface IKeycloakAdmin
{
    /// <summary>Every WebAuthn credential on the account, newest first.</summary>
    Task<IReadOnlyList<WebAuthnCredential>> ListWebAuthnCredentialsAsync(
        string realm, string subject, CancellationToken ct = default);

    /// <summary>
    /// Removes one credential. Returns false when Keycloak no longer has it — a double-submitted
    /// delete, or one the user removed from Keycloak's own account console meanwhile — which is
    /// the caller's cue to report success rather than an error: the credential is gone either way.
    /// </summary>
    Task<bool> DeleteCredentialAsync(
        string realm, string subject, string credentialId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the Keycloak user, taking their credentials and SSO sessions with it. Idempotent:
    /// a user that is already gone is not an error.
    /// </summary>
    Task DeleteUserAsync(string realm, string subject, CancellationToken ct = default);

    /// <summary>
    /// Whether some <em>other</em> account in the realm already holds <paramref name="email"/> —
    /// asked before a confirmation code is mailed, so a user learns the address is unavailable
    /// while they can still correct it rather than after proving they read that mailbox.
    ///
    /// <para>Advisory only. Nothing locks the address between this call and the write, so
    /// <see cref="UpdateEmailAsync"/> answering <see cref="EmailUpdateOutcome.AddressTaken"/>
    /// stays the authoritative answer; this one spares the user the round trip in the ordinary
    /// case.</para>
    /// </summary>
    /// <param name="exceptSubject">The account doing the asking — its own record matching is not a
    /// conflict.</param>
    Task<bool> IsEmailTakenAsync(
        string realm, string email, string exceptSubject, CancellationToken ct = default);

    /// <summary>
    /// Writes a new email address onto the account, marking it verified — this is only ever
    /// called once auth-svc has proven the user reads that mailbox.
    ///
    /// <para>The username goes with it. The realm has
    /// <c>registrationEmailAsUsername</c>, so the address <em>is</em> the username, and leaving
    /// the two apart would leave the user signing in with an address that no longer reaches
    /// them.</para>
    /// </summary>
    Task<EmailUpdateOutcome> UpdateEmailAsync(
        string realm, string subject, string email, CancellationToken ct = default);
}

/// <summary>How <see cref="IKeycloakAdmin.UpdateEmailAsync"/> ended.</summary>
public enum EmailUpdateOutcome
{
    Updated,

    /// <summary>Another account in the realm already holds the address
    /// (<c>duplicateEmailsAllowed</c> is off). Nothing was written. Reached even though
    /// <see cref="IKeycloakAdmin.IsEmailTakenAsync"/> was asked first, when the address was
    /// claimed in between.</summary>
    AddressTaken,

    /// <summary>The Keycloak user is gone — deleted from under a session that outlived it.</summary>
    UserGone,
}
