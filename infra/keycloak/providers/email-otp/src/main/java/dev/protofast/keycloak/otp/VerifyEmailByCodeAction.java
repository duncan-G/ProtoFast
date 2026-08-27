package dev.protofast.keycloak.otp;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.InitiatedActionSupport;
import org.keycloak.authentication.RequiredActionContext;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.events.EventType;
import org.keycloak.models.UserModel;
import org.keycloak.services.managers.BruteForceProtector;

import dev.protofast.keycloak.signup.SignupClaims;

/**
 * Proves the address at sign-up by asking for a mailed code instead of a clicked link.
 *
 * <p>A link strands people: registering on a laptop and opening the mail on a phone
 * verifies the address but mints the session on the phone, where the user was not. A
 * code is readable anywhere and typed back into the tab they started in. It also makes
 * link-prefetching mail scanners harmless — they cannot consume a code the way they
 * consume a single-use action token by fetching it.
 */
public class VerifyEmailByCodeAction implements RequiredActionProvider {

    @Override
    public InitiatedActionSupport initiatedActionSupport() {
        // Reachable as kc_action, so an address that needs re-proving can be handled
        // without waiting for the next sign-in to trigger it.
        return InitiatedActionSupport.SUPPORTED;
    }

    @Override
    public void evaluateTriggers(RequiredActionContext context) {
        if (context.getRealm().isVerifyEmail() && !context.getUser().isEmailVerified()) {
            context.getUser().addRequiredAction(VerifyEmailByCodeActionFactory.PROVIDER_ID);
        }
    }

    @Override
    public void requiredActionChallenge(RequiredActionContext context) {
        UserModel user = context.getUser();
        if (user.isEmailVerified()) {
            done(context);
            return;
        }

        EmailOtpService codes = codes(context);
        String error = null;
        if (!codes.hasLiveCode(user)) {
            error = EmailOtpForm.message(codes.send(
                    user, EmailOtpForm.MAIL_VERIFY_EMAIL_SUBJECT, EmailOtpForm.MAIL_VERIFY_EMAIL));
        }

        context.challenge(page(context, codes, error));
    }

    @Override
    public void processAction(RequiredActionContext context) {
        UserModel user = context.getUser();
        MultivaluedMap<String, String> form = context.getHttpRequest().getDecodedFormParameters();
        EmailOtpService codes = codes(context);

        if (EmailOtpForm.ACTION_RESEND.equals(form.getFirst(EmailOtpForm.FIELD_ACTION))) {
            String error = EmailOtpForm.message(codes.send(
                    user, EmailOtpForm.MAIL_VERIFY_EMAIL_SUBJECT, EmailOtpForm.MAIL_VERIFY_EMAIL));
            context.challenge(page(context, codes, error));
            return;
        }

        EmailOtpService.VerifyResult result = codes.verify(user, form.getFirst(EmailOtpForm.FIELD_CODE));
        if (result == EmailOtpService.VerifyResult.VALID) {
            user.setEmailVerified(true);
            SignupClaims.clear(user);
            EmailOtpService.clearThrottle(user);
            done(context);
            return;
        }

        context.getEvent().clone().event(EventType.VERIFY_EMAIL)
                .detail(Details.EMAIL, user.getEmail())
                .error(Errors.INVALID_USER_CREDENTIALS);
        // Same counter a wrong sign-in code feeds: this address is a way into the
        // account too, so guessing here has to cost what guessing there does.
        if (context.getRealm().isBruteForceProtected()) {
            context.getSession().getProvider(BruteForceProtector.class).failedLogin(
                    context.getRealm(), user, context.getConnection(), context.getUriInfo(),
                    EmailOtpAuthenticatorFactory.BRUTE_FORCE_CATEGORIES);
        }

        context.challenge(page(context, codes, EmailOtpForm.message(result)));
    }

    @Override
    public void close() {
    }

    private static void done(RequiredActionContext context) {
        UserModel user = context.getUser();
        user.removeRequiredAction(VerifyEmailByCodeActionFactory.PROVIDER_ID);
        // Drop Keycloak's own action too. The realm disables it, but a user provisioned
        // before that (or by an admin) can still be carrying one, and it would send the
        // link this action exists to replace.
        user.removeRequiredAction(UserModel.RequiredAction.VERIFY_EMAIL);
        context.getAuthenticationSession().removeRequiredAction(VerifyEmailByCodeActionFactory.PROVIDER_ID);
        context.getAuthenticationSession().removeRequiredAction(UserModel.RequiredAction.VERIFY_EMAIL);
        context.getEvent().clone().event(EventType.VERIFY_EMAIL)
                .detail(Details.EMAIL, user.getEmail())
                .success();
        context.success();
    }

    private static EmailOtpService codes(RequiredActionContext context) {
        return new EmailOtpService(context.getSession(), context.getRealm(), context.getAuthenticationSession());
    }

    private static Response page(RequiredActionContext context, EmailOtpService codes, String error) {
        var form = EmailOtpForm.prepare(context.form(), codes, context.getUser());
        if (error != null) {
            form.setError(error, String.valueOf(codes.secondsUntilResend(context.getUser())));
        }
        return form.createForm(EmailOtpForm.PAGE_VERIFY_EMAIL);
    }
}
