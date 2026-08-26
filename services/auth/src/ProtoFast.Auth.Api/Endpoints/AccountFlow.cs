using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProtoFast.Auth.Api.Accounts;
using ProtoFast.Auth.Api.Configuration;
using ProtoFast.Auth.Api.Email;
using ProtoFast.Auth.Api.Keycloak;
using ProtoFast.Auth.Api.Sessions;
using ProtoFast.Auth.Data;

namespace ProtoFast.Auth.Api.Endpoints;

/// <summary>What the account page renders: the address the account is reached at, the credentials
/// that reach it, and an email change waiting on its code.</summary>
/// <param name="PasskeysUnavailable">True when Keycloak could not be asked for the credential
/// list. The page still has an email address and a way out of the account, so this reports the
/// gap rather than failing the whole request.</param>
/// <param name="PendingEmail">The address a code has been mailed to, or null when no change is in
/// flight. Carried on the view so a reloaded page — or the same account in another tab — comes
/// back to the code box rather than pretending nothing was started.</param>
public sealed record AccountView(
    string Email,
    string Tenant,
    IReadOnlyList<AccountPasskey> Passkeys,
    bool PasskeysUnavailable,
    string? PendingEmail = null,
    DateTimeOffset? PendingEmailExpiresAt = null);

/// <param name="Passwordless">A passkey, as opposed to a second-factor WebAuthn credential. The
/// realm only enrols the former; the flag exists so the page never silently relabels the latter.</param>
public sealed record AccountPasskey(string Id, string Label, DateTimeOffset? CreatedAt, bool Passwordless);

/// <summary>Body of <c>POST /account/email</c> — the address to move the account to.</summary>
public sealed record EmailChangeRequest(string? NewEmail);

/// <summary>Body of <c>POST /account/email/confirm</c> — the code that was mailed to it.</summary>
public sealed record EmailChangeConfirmation(string? Code);

/// <summary>What <c>POST /account/email</c> answers with: where the code went, and how long it
/// is good for.</summary>
public sealed record PendingEmailChangeView(string Email, DateTimeOffset ExpiresAt);

/// <summary>
/// The account-management endpoints behind <c>/account/*</c> — what a signed-in user can do to
/// their own account without an operator.
///
/// <para>All of it happens on our own origin. Keycloak's account console is never linked to and
/// is expected to be unreachable from the internet: changing an email address is an ordinary
/// verified write, and auth-svc does the verifying — a code to the new address, typed back on the
/// account page, and only then the Admin API write. The one thing still handed to Keycloak is
/// enrolling a passkey, because a WebAuthn ceremony needs Keycloak's own origin and there is no
/// API that stands in for it.</para>
///
/// <para>These run with ext_authz OFF, exactly like the sign-in endpoints, so nothing upstream
/// vouches for the caller: every method here resolves the session cookie itself and answers 401
/// when it does not yield an identity. The identity headers Envoy injects elsewhere are never
/// read, because on this route they are whatever the client sent.</para>
///
/// <para>Scoped — it holds the request's <see cref="AuthDbContext"/>.</para>
/// </summary>
public sealed class AccountFlow(
    IKeycloakAdmin admin,
    IEmailChangeStore emailChanges,
    IEmailSender mail,
    ISessionStore sessionStore,
    SessionResolver sessionResolver,
    AuthDbContext db,
    TimeProvider clock,
    IOptions<SessionPolicyOptions> sessionOptions,
    ILogger<AccountFlow> logger)
{
    private readonly SessionPolicyOptions _session = sessionOptions.Value;

    /// <summary>GET /account/me — the account as the page needs to render it.</summary>
    public async Task<IResult> GetAsync(HttpContext ctx, CancellationToken ct)
    {
        var session = await AuthenticateAsync(ctx, ct);
        if (session is null)
        {
            return Unauthorized();
        }

        NoStore(ctx);

        IReadOnlyList<WebAuthnCredential> credentials = [];
        var unavailable = false;
        try
        {
            credentials = await admin.ListWebAuthnCredentialsAsync(session.Realm, session.Subject, ct);
        }
        catch (Exception ex)
        {
            // Keycloak is unreachable, or no admin client is configured for this deployment. The
            // rest of the page is still true and still useful, so say so and render it.
            logger.LogError(ex, "Could not list credentials for realm {Realm}", session.Realm);
            unavailable = true;
        }

        var pending = await TryGetPendingAsync(session, ct);

        return Results.Ok(new AccountView(
            session.Data.Email,
            session.Realm,
            credentials
                .Select(c => new AccountPasskey(c.Id, c.Label, c.CreatedAt, c.Passwordless))
                .ToArray(),
            unavailable,
            pending?.NewEmail,
            pending?.ExpiresAt));
    }

    /// <summary>
    /// POST /account/email — start a change by mailing a code to the address being claimed.
    ///
    /// <para>Nothing is written to Keycloak here. The address on the account is also the username
    /// and the only thing an emailed sign-in code can be sent to, so committing it before the user
    /// has read that mailbox would turn a typo into a permanent lockout. The pending change lives
    /// in Redis until the code comes back.</para>
    ///
    /// <para>An address another account already holds is refused here, before any mail goes out:
    /// a user who mistypes someone else's address should learn it while they can still fix it,
    /// not after fetching a code that was never going to commit. Confirm time checks again, and
    /// remains the authoritative answer — nothing reserves the address in between.</para>
    /// </summary>
    public async Task<IResult> RequestEmailChangeAsync(HttpContext ctx, CancellationToken ct)
    {
        if (!IsSameOrigin(ctx))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var session = await AuthenticateAsync(ctx, ct);
        if (session is null)
        {
            return Unauthorized();
        }

        NoStore(ctx);

        var body = await ReadBodyAsync<EmailChangeRequest>(ctx, ct);
        if (!EmailAddress.TryNormalize(body?.NewEmail, out var newEmail))
        {
            return Problem("invalid_email", "That doesn't look like an email address.", StatusCodes.Status400BadRequest);
        }

        if (string.Equals(newEmail, session.Data.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                "email_unchanged",
                "That is already the address on your account.",
                StatusCodes.Status400BadRequest);
        }

        // Ahead of the cooldown, so a typo that lands on a taken address does not cost the user a
        // minute before they can try the right one. It does mean an unthrottled answer to "does
        // this address have an account here", which the confirm-time conflict gave up eventually
        // anyway; the throttle guards the mailbox, and no mail is sent on this path.
        bool taken;
        try
        {
            taken = await admin.IsEmailTakenAsync(session.Realm, newEmail, session.Subject, ct);
        }
        catch (Exception ex)
        {
            // No point mailing a code for a change that could not be written either.
            logger.LogError(ex, "Could not check whether an address is taken in realm {Realm}", session.Realm);
            return KeycloakUnavailable("Your email address could not be changed. Please try again.");
        }

        if (taken)
        {
            return Problem(
                "email_taken",
                "That address already belongs to another account.",
                StatusCodes.Status409Conflict);
        }

        if (!mail.IsConfigured)
        {
            logger.LogError("An email change was requested but no SMTP relay is configured");
            return Problem(
                "mail_unavailable",
                "We can't send confirmation codes right now. Please try again later.",
                StatusCodes.Status503ServiceUnavailable);
        }

        // The mailbox being claimed belongs to someone who did not ask for any of this, and one
        // session must not be able to keep writing to it.
        if (!await emailChanges.TryTakeSendSlotAsync(
                session.Realm, session.Subject, EmailChangeCode.RequestCooldown, ct))
        {
            return Problem(
                "too_soon",
                "A code was just sent. Wait a minute before asking for another.",
                StatusCodes.Status429TooManyRequests);
        }

        var code = EmailChangeCode.Generate();
        var salt = EmailChangeCode.NewSalt();
        var now = clock.GetUtcNow();
        var pending = new PendingEmailChange(
            newEmail, salt, EmailChangeCode.Hash(salt, code), now, now + EmailChangeCode.Lifetime);

        // Stored before it is sent, so a mailed code is never one this service has no record of.
        // If the send then fails, the record goes with it rather than leaving the page waiting on
        // a code that is not coming.
        await emailChanges.SaveAsync(session.Realm, session.Subject, pending, ct);

        try
        {
            var (text, html) = ConfirmationMessage(code, newEmail);
            await mail.SendAsync(
                new EmailMessage(newEmail, "Confirm your new email address", text, html), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not mail an email-change code in realm {Realm}", session.Realm);
            await SafeDeletePendingAsync(session, ct);
            return Problem(
                "mail_unavailable",
                "We couldn't send the code. Please try again in a moment.",
                StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted(value: new PendingEmailChangeView(newEmail, pending.ExpiresAt));
    }

    /// <summary>
    /// POST /account/email/confirm — the code came back, so commit the change.
    ///
    /// <para>The address written is the one held in Redis, never one the request restates: the
    /// code proves the user read <em>that</em> mailbox and nothing else.</para>
    /// </summary>
    public async Task<IResult> ConfirmEmailChangeAsync(HttpContext ctx, CancellationToken ct)
    {
        if (!IsSameOrigin(ctx))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var session = await AuthenticateAsync(ctx, ct);
        if (session is null)
        {
            return Unauthorized();
        }

        NoStore(ctx);

        var pending = await emailChanges.GetAsync(session.Realm, session.Subject, ct);
        if (pending is null)
        {
            return Problem(
                "no_pending_change",
                "There's no email change waiting. Start again.",
                StatusCodes.Status404NotFound);
        }

        // Redis expires the record on its own; this catches the moment in between, and a store
        // that ever loses its TTL.
        if (pending.ExpiresAt <= clock.GetUtcNow())
        {
            await SafeDeletePendingAsync(session, ct);
            return Problem("code_expired", "That code has expired. Start again.", StatusCodes.Status410Gone);
        }

        var body = await ReadBodyAsync<EmailChangeConfirmation>(ctx, ct);
        if (!EmailChangeCode.Matches(pending, (body?.Code ?? "").Trim()))
        {
            return await WrongCodeAsync(session, pending, ct);
        }

        EmailUpdateOutcome outcome;
        try
        {
            outcome = await admin.UpdateEmailAsync(session.Realm, session.Subject, pending.NewEmail, ct);
        }
        catch (Exception ex)
        {
            // The code stays valid: nothing was written, and the user should be able to press the
            // button again rather than start over.
            logger.LogError(ex, "Could not write the new email address in realm {Realm}", session.Realm);
            return KeycloakUnavailable("Your email address could not be changed. Please try again.");
        }

        switch (outcome)
        {
            case EmailUpdateOutcome.AddressTaken:
                // Asked and answered when the change was started; reaching it here means the
                // address was claimed inside the fifteen minutes, and Keycloak's own uniqueness
                // check is the only one that cannot be raced.
                await SafeDeletePendingAsync(session, ct);
                return Problem(
                    "email_taken",
                    "That address already belongs to another account.",
                    StatusCodes.Status409Conflict);

            case EmailUpdateOutcome.UserGone:
                // The account was deleted out from under this session. Nothing to change, and
                // nothing left to be signed in to.
                logger.LogWarning(
                    "An email change targeted a Keycloak user that no longer exists in realm {Realm}",
                    session.Realm);
                await SafeDeletePendingAsync(session, ct);
                await DropSessionsAsync(ctx, session, ct);
                return Unauthorized();
        }

        Activity.Current?.SetTag("auth.realm", session.Realm);
        await CommitEmailChangeAsync(session, pending.NewEmail, ct);
        return Results.NoContent();
    }

    /// <summary>DELETE /account/email — abandon a change that has not been confirmed.</summary>
    public async Task<IResult> CancelEmailChangeAsync(HttpContext ctx, CancellationToken ct)
    {
        if (!IsSameOrigin(ctx))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var session = await AuthenticateAsync(ctx, ct);
        if (session is null)
        {
            return Unauthorized();
        }

        await emailChanges.DeleteAsync(session.Realm, session.Subject, ct);

        NoStore(ctx);
        return Results.NoContent();
    }

    /// <summary>
    /// DELETE /account/passkeys/{credentialId} — remove one credential.
    ///
    /// <para>Removing the last one locks nobody out: the realm has no passwords and the emailed
    /// code is a first-class sign-in method for every account (see the realm README). What it does
    /// mean is that the sign-in offer should come back, so the local "has a passkey" stamp is
    /// cleared once the last passkey is gone.</para>
    /// </summary>
    public async Task<IResult> DeletePasskeyAsync(HttpContext ctx, string credentialId, CancellationToken ct)
    {
        if (!IsSameOrigin(ctx))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var session = await AuthenticateAsync(ctx, ct);
        if (session is null)
        {
            return Unauthorized();
        }

        try
        {
            // A credential Keycloak no longer has is the outcome the caller asked for, so a
            // "not found" is reported as success — a double-tapped button is not an error.
            await admin.DeleteCredentialAsync(session.Realm, session.Subject, credentialId, ct);
            await SyncPasskeyStampAsync(session, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not delete credential for realm {Realm}", session.Realm);
            return KeycloakUnavailable("The passkey could not be removed. Please try again.");
        }

        NoStore(ctx);
        return Results.NoContent();
    }

    /// <summary>
    /// POST /account/delete — erase the account.
    ///
    /// <para>Today that is genuinely everything: the Keycloak user (which takes its credentials
    /// and SSO sessions with it), the local <c>UserAccount</c> row, and every BFF session the
    /// browser holds. When there is user-owned data in other services, deleting it belongs
    /// <em>here</em>, ahead of the identity — this endpoint is the only thing that knows the
    /// account is going away.</para>
    ///
    /// <para>Keycloak goes first because it is the identity of record. If it refuses, nothing else
    /// has happened yet and the user can try again; the reverse order would leave an account that
    /// can still sign in and would simply be re-provisioned on the next callback, quietly undoing
    /// the deletion the user asked for.</para>
    /// </summary>
    public async Task<IResult> DeleteAccountAsync(HttpContext ctx, CancellationToken ct)
    {
        if (!IsSameOrigin(ctx))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var session = await AuthenticateAsync(ctx, ct);
        if (session is null)
        {
            return Unauthorized();
        }

        Activity.Current?.SetTag("auth.realm", session.Realm);

        try
        {
            await admin.DeleteUserAsync(session.Realm, session.Subject, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not delete the Keycloak user in realm {Realm}", session.Realm);
            return KeycloakUnavailable("Your account could not be deleted. Please try again.");
        }

        // Past this point the identity is gone and the user can never sign in again, so every
        // remaining step is cleanup that must not be abandoned on the first failure — a leftover
        // row is a mess to reconcile, but a half-deleted account the user still holds a live
        // session for is worse.
        try
        {
            await db.Users
                .Where(u => u.Realm == session.Realm && u.Subject == session.Subject)
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Keycloak user deleted but the local row survived in realm {Realm}; it is now orphaned",
                session.Realm);
        }

        await SafeDeletePendingAsync(session, ct);
        await DropSessionsAsync(ctx, session, ct);

        NoStore(ctx);
        return Results.NoContent();
    }

    /// <summary>
    /// Everything after Keycloak has accepted the new address. None of it may fail the request:
    /// the address has already moved, and the user is owed the page saying so. Each step that
    /// lags is a step that heals — the local row and the session both re-read from the token on
    /// the next refresh.
    /// </summary>
    private async Task CommitEmailChangeAsync(AuthenticatedSession session, string newEmail, CancellationToken ct)
    {
        var previousEmail = session.Data.Email;

        await SafeDeletePendingAsync(session, ct);

        try
        {
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Realm == session.Realm && u.Subject == session.Subject, ct);
            if (user is not null)
            {
                user.Email = newEmail;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email changed in Keycloak but the local row lagged in realm {Realm}", session.Realm);
        }

        try
        {
            // This browser's session only. A session on the sibling host keeps the old address
            // until its next refresh re-reads the token, which is the same lag every other
            // Keycloak-owned fact on that record has.
            await sessionStore.UpdateAsync(session.SessionId, session.Data with { Email = newEmail }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not refresh the session's email in realm {Realm}", session.Realm);
        }

        await NotifyPreviousAddressAsync(previousEmail, newEmail, ct);
    }

    /// <summary>
    /// Tells the address that just lost the account that it happened. Nobody asked for this mail
    /// and it carries no action, which is the point: it is the tripwire for a change the account's
    /// owner did not make, and the only warning they would otherwise get is silence.
    /// </summary>
    private async Task NotifyPreviousAddressAsync(string previousEmail, string newEmail, CancellationToken ct)
    {
        if (!mail.IsConfigured || string.IsNullOrEmpty(previousEmail))
        {
            return;
        }

        try
        {
            var (text, html) = ChangedNoticeMessage(previousEmail, newEmail);
            await mail.SendAsync(
                new EmailMessage(previousEmail, "Your Protofast email address was changed", text, html),
                ct);
        }
        catch (Exception ex)
        {
            // The change stands regardless; a heads-up that could not be sent is not a reason to
            // tell the user their address did not move.
            logger.LogError(ex, "Could not notify the previous address of an email change");
        }
    }

    /// <summary>
    /// Books a wrong guess and decides whether the change survives it. Six digits is a small
    /// space, so the fifth wrong code ends the attempt outright rather than throttling it.
    /// </summary>
    private async Task<IResult> WrongCodeAsync(
        AuthenticatedSession session, PendingEmailChange pending, CancellationToken ct)
    {
        var attempts = pending.Attempts + 1;
        if (attempts >= EmailChangeCode.MaxAttempts)
        {
            await SafeDeletePendingAsync(session, ct);
            return Problem(
                "too_many_attempts",
                "Too many wrong codes. Start the change again.",
                StatusCodes.Status400BadRequest);
        }

        // Re-saved with the original ExpiresAt, so guessing cannot extend the window.
        await emailChanges.SaveAsync(session.Realm, session.Subject, pending with { Attempts = attempts }, ct);

        var left = EmailChangeCode.MaxAttempts - attempts;
        return Problem(
            "invalid_code",
            $"That code isn't right. {left} {(left == 1 ? "attempt" : "attempts")} left.",
            StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// The code mail, in both parts.
    ///
    /// <para>The two parts say the same thing in the same order — the text one is not a stub. It
    /// is what a client that refuses HTML shows, and a code is the one thing in this service's
    /// mail that has to arrive readable everywhere.</para>
    /// </summary>
    private (string Text, string Html) ConfirmationMessage(string code, string newEmail)
    {
        var minutes = $"{EmailChangeCode.Lifetime.TotalMinutes:0}";

        var lead = "Use this code to confirm your new Protofast email address:";
        var expiry = $"It expires in {minutes} minutes. Enter it on the account page you started "
            + "the change from.";
        var ignore = "If you didn't ask to change your email address, ignore this — nothing has "
            + "changed, and this address has not been added to any account.";

        var text = $"""
                    {lead}

                        {code}

                    {expiry}

                    {ignore}
                    """;

        var html = NocturneEmail.Page(
            "Confirm your new address",
            "Your code for confirming this address on Protofast.",
            newEmail,
            clock.GetUtcNow().Year,
            NocturneEmail.Text(lead),
            NocturneEmail.Code(code),
            NocturneEmail.Note(expiry),
            NocturneEmail.Note(ignore));

        return (text, html);
    }

    /// <summary>The heads-up to the address that just lost the account, in both parts.</summary>
    private (string Text, string Html) ChangedNoticeMessage(string previousEmail, string newEmail)
    {
        var lead = $"The email address on your Protofast account was changed to {newEmail}.";
        var consequence = "Sign-in codes now go to that address, and this one no longer reaches "
            + "the account.";
        var warning = "If this wasn't you, contact us straight away — whoever made the change can "
            + "sign in.";

        var text = $"""
                    {lead}

                    {consequence}

                    {warning}
                    """;

        var html = NocturneEmail.Page(
            "Your email address was changed",
            "The address on your Protofast account was changed.",
            previousEmail,
            clock.GetUtcNow().Year,
            NocturneEmail.Text(lead),
            NocturneEmail.Text(consequence),
            NocturneEmail.Rule(),
            NocturneEmail.Text(warning));

        return (text, html);
    }

    /// <summary>
    /// The pending change for the page, or null. A store that is down must not take the whole
    /// account page with it: the rest of the view is still true.
    /// </summary>
    private async Task<PendingEmailChange?> TryGetPendingAsync(AuthenticatedSession session, CancellationToken ct)
    {
        try
        {
            return await emailChanges.GetAsync(session.Realm, session.Subject, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the pending email change in realm {Realm}", session.Realm);
            return null;
        }
    }

    /// <summary>
    /// Clears the pending change on a path that has already decided what to answer. A record that
    /// outlives its purpose expires by itself within the quarter hour, so failing to remove it now
    /// is never worth changing the response for.
    /// </summary>
    private async Task SafeDeletePendingAsync(AuthenticatedSession session, CancellationToken ct)
    {
        try
        {
            await emailChanges.DeleteAsync(session.Realm, session.Subject, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not clear a pending email change in realm {Realm}", session.Realm);
        }
    }

    /// <summary>
    /// Drops every session this browser holds for the deleted account. Keycloak's back-channel
    /// logout reaches the sibling host's session too, but only once it gets round to it; the
    /// user is standing in front of the page now.
    /// </summary>
    private async Task DropSessionsAsync(HttpContext ctx, AuthenticatedSession session, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrEmpty(session.Data.KcSessionId))
            {
                await sessionStore.DeleteByKeycloakSessionAsync(session.Realm, session.Data.KcSessionId, ct);
            }

            await sessionStore.DeleteAsync(session.SessionId, ct);
        }
        catch (Exception ex)
        {
            // Redis is unreachable. The session can no longer refresh — its Keycloak user is gone —
            // so it dies at the next resolve regardless; clearing the cookie is what the browser sees.
            logger.LogError(ex, "Could not erase sessions for the deleted account in realm {Realm}", session.Realm);
        }

        SessionCookie.Clear(ctx, _session);
    }

    /// <summary>
    /// Re-reads the credential list after a removal and keeps <c>UserAccount.PasskeyRegisteredAt</c>
    /// honest: null again once no passkey remains, so the sign-in offer starts coming back.
    /// </summary>
    private async Task SyncPasskeyStampAsync(AuthenticatedSession session, CancellationToken ct)
    {
        var remaining = await admin.ListWebAuthnCredentialsAsync(session.Realm, session.Subject, ct);
        if (remaining.Any(c => c.Passwordless))
        {
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Realm == session.Realm && u.Subject == session.Subject, ct);
        if (user?.PasskeyRegisteredAt is null)
        {
            return;
        }

        user.PasskeyRegisteredAt = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The session behind this request, or null when there isn't one. Resolution is the same as
    /// <c>Check</c>'s — a record in the store is not enough, it has to still yield an identity —
    /// and a rotated id is re-issued as a cookie here just as it is on the sign-in path.
    /// </summary>
    private async Task<AuthenticatedSession?> AuthenticateAsync(HttpContext ctx, CancellationToken ct)
    {
        var sessionId = ctx.Request.Cookies[_session.CookieName];
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        ResolvedIdentity? identity;
        try
        {
            identity = await sessionResolver.ResolveSessionAsync(sessionId, ctx.Request.Host.Value, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Session resolution failed on an account endpoint; treating as anonymous");
            return null;
        }

        if (identity is null)
        {
            SessionCookie.Clear(ctx, _session);
            return null;
        }

        if (identity.RotatedSessionId is not null)
        {
            sessionId = identity.RotatedSessionId;
            SessionCookie.Append(ctx, _session, sessionId);
        }

        var data = await sessionStore.GetAsync(sessionId, ct);
        return data is null ? null : new AuthenticatedSession(sessionId, data);
    }

    /// <summary>
    /// The request's JSON body, or null when there isn't one that parses. Read here rather than
    /// bound by the framework so that the session check runs first: a caller with no session gets
    /// 401 for a malformed body just as it does for a well-formed one, and never learns which of
    /// the two it got wrong.
    /// </summary>
    private static async Task<T?> ReadBodyAsync<T>(HttpContext ctx, CancellationToken ct) where T : class
    {
        try
        {
            return await ctx.Request.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or BadHttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rejects a cross-site write. The session cookie is <c>SameSite=Lax</c>, so a cross-site
    /// POST or DELETE never carries it in the first place — this is the belt to that braces, and
    /// it costs one header comparison. A same-origin <c>fetch</c> always sends <c>Origin</c>;
    /// a request without one did not come from a page.
    /// </summary>
    private static bool IsSameOrigin(HttpContext ctx)
    {
        var origin = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
               && string.Equals(parsed.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static IResult Unauthorized() =>
        Results.Json(new { error = "not_signed_in" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult KeycloakUnavailable(string message) =>
        Results.Json(new { error = "keycloak_unavailable", message }, statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>The shape every refusal on these endpoints takes: a code the client can branch on
    /// and a sentence it can render unchanged.</summary>
    private static IResult Problem(string error, string message, int statusCode) =>
        Results.Json(new { error, message }, statusCode: statusCode);

    /// <summary>Everything here is per-account and momentary; none of it may be cached anywhere.</summary>
    private static void NoStore(HttpContext ctx) =>
        ctx.Response.Headers.CacheControl = "private, no-store";

    private sealed record AuthenticatedSession(string SessionId, SessionData Data)
    {
        public string Realm => Data.Realm;

        public string Subject => Data.Sub;
    }
}
