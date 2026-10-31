package dev.protofast.keycloak.otp;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.Authenticator;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.services.managers.BruteForceProtector;
import org.keycloak.services.messages.Messages;

/**
 * Signs a user in with a code mailed to their address — the floor credential of every
 * account, and the only one that works on a device where no passkey is enrolled.
 *
 * <p>It reports itself configured for everybody on purpose. Gating it behind "has no
 * other credential" would lock out exactly the people it exists for: a passkey holder
 * on a new laptop has no passkey <em>there</em>, and the alternative they need has to
 * be reachable from the credential chooser.
 *
 * <p>Wrong codes are both counted locally (the code is destroyed after a handful) and
 * reported to the realm's brute-force protector, so guessing costs an attacker the
 * account lockout that guessing a password would have.
 */
public class EmailOtpAuthenticator implements Authenticator {

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        UserModel user = context.getUser();
        if (user == null) {
            // The username form establishes the user before this step. Reaching here
            // without one means the step is wired somewhere it does not belong.
            context.attempted();
            return;
        }

        if (isTemporarilyDisabled(context.getSession(), context.getRealm(), user)) {
            lockedOut(context, user);
            return;
        }

        EmailOtpService codes = codes(context);
        String error = null;

        // A code already in flight is reused: "Try another way" can bring the user back
        // here, and re-sending on every visit would burn the per-session send budget.
        if (!codes.hasLiveCode(user)) {
            error = EmailOtpForm.message(codes.send(
                    user, EmailOtpForm.MAIL_SIGN_IN_SUBJECT, EmailOtpForm.MAIL_SIGN_IN));
        }

        context.challenge(page(context, codes, user, error));
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        UserModel user = context.getUser();
        MultivaluedMap<String, String> form = context.getHttpRequest().getDecodedFormParameters();
        EmailOtpService codes = codes(context);

        if (EmailOtpForm.ACTION_RESEND.equals(form.getFirst(EmailOtpForm.FIELD_ACTION))) {
            String error = EmailOtpForm.message(codes.send(
                    user, EmailOtpForm.MAIL_SIGN_IN_SUBJECT, EmailOtpForm.MAIL_SIGN_IN));
            context.challenge(page(context, codes, user, error));
            return;
        }

        if (isTemporarilyDisabled(context.getSession(), context.getRealm(), user)) {
            lockedOut(context, user);
            return;
        }

        EmailOtpService.VerifyResult result = codes.verify(user, form.getFirst(EmailOtpForm.FIELD_CODE));
        if (result == EmailOtpService.VerifyResult.VALID) {
            context.getEvent().detail(Details.AUTH_METHOD, EmailOtpAuthenticatorFactory.PROVIDER_ID);
            context.success();
            return;
        }

        registerFailure(context, user);
        context.failureChallenge(
                AuthenticationFlowError.INVALID_CREDENTIALS,
                page(context, codes, user, EmailOtpForm.message(result)));
    }

    @Override
    public boolean requiresUser() {
        return true;
    }

    /**
     * Always available. This is what makes the mailed code an alternative rather than a
     * fallback branch — see the class comment.
     */
    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
        // Nothing to enrol: the address is the credential.
    }

    @Override
    public void close() {
    }

    private static EmailOtpService codes(AuthenticationFlowContext context) {
        return new EmailOtpService(context.getSession(), context.getRealm(), context.getAuthenticationSession());
    }

    private static Response page(AuthenticationFlowContext context, EmailOtpService codes, UserModel user, String error) {
        var form = EmailOtpForm.prepare(context.form(), codes, user);
        if (error != null) {
            form.setError(error, String.valueOf(codes.secondsUntilResend(user)));
        }
        return form.createForm(EmailOtpForm.PAGE_SIGN_IN);
    }

    /**
     * Same wording a locked-out password attempt gets, deliberately: saying "this account
     * is locked" tells an attacker their guessing is landing on a real account.
     */
    private static void lockedOut(AuthenticationFlowContext context, UserModel user) {
        context.getEvent().user(user).error(Errors.USER_TEMPORARILY_DISABLED);
        context.failureChallenge(
                AuthenticationFlowError.USER_TEMPORARILY_DISABLED,
                context.form().setError(Messages.INVALID_USER).createErrorPage(Response.Status.BAD_REQUEST));
    }

    private static void registerFailure(AuthenticationFlowContext context, UserModel user) {
        context.getEvent().user(user).error(Errors.INVALID_USER_CREDENTIALS);
        if (context.getRealm().isBruteForceProtected()) {
            context.getSession().getProvider(BruteForceProtector.class).failedLogin(
                    context.getRealm(), user, context.getConnection(), context.getUriInfo(),
                    EmailOtpAuthenticatorFactory.BRUTE_FORCE_CATEGORIES);
        }
    }

    private static boolean isTemporarilyDisabled(KeycloakSession session, RealmModel realm, UserModel user) {
        return realm.isBruteForceProtected()
                && session.getProvider(BruteForceProtector.class).isTemporarilyDisabled(session, realm, user);
    }
}
