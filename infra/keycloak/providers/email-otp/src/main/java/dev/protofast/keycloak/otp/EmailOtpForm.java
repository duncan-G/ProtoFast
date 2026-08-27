package dev.protofast.keycloak.otp;

import org.keycloak.forms.login.LoginFormsProvider;
import org.keycloak.models.UserModel;

/**
 * The bits the sign-in step and the verify-email step share: form field names, the
 * templates, and the mapping from a {@link EmailOtpService} outcome to a message key.
 *
 * <p>Two Freemarker templates, one per entry point, both shipped inside this JAR as
 * theme resources. They {@code <#import "template.ftl">}, which FreeMarker resolves
 * through the theme chain — so they render inside whichever login theme the realm is
 * using and pick up its classes without this JAR knowing anything about it.
 */
final class EmailOtpForm {

    static final String FIELD_CODE = "code";
    static final String FIELD_ACTION = "otpAction";
    static final String ACTION_RESEND = "resend";

    /** Login-page templates (theme-resources/templates). */
    static final String PAGE_SIGN_IN = "login-email-otp.ftl";
    static final String PAGE_VERIFY_EMAIL = "verify-email-otp.ftl";

    /** Mail templates, which live in the realm's email theme so copy edits need no rebuild. */
    static final String MAIL_SIGN_IN = "login-code.ftl";
    static final String MAIL_SIGN_IN_SUBJECT = "pfSignInCodeSubject";
    static final String MAIL_VERIFY_EMAIL = "email-verification-with-code.ftl";
    static final String MAIL_VERIFY_EMAIL_SUBJECT = "emailVerificationSubject";

    private EmailOtpForm() {
    }

    /** Everything the templates read, set the same way for both pages. */
    static LoginFormsProvider prepare(LoginFormsProvider form, EmailOtpService codes, UserModel user) {
        return form
                .setAttribute("otpEmail", user.getEmail() == null ? "" : user.getEmail())
                .setAttribute("otpCodeLength", EmailOtpService.CODE_DIGITS)
                .setAttribute("otpResendAllowed", codes.resendAllowed(user))
                .setAttribute("otpResendIn", String.valueOf(codes.secondsUntilResend(user)))
                .setAttribute("otpSendsLeft", EmailOtpService.MAX_SENDS);
    }

    /**
     * What to tell the user about a send that did not simply work. Null for
     * {@link EmailOtpService.SendResult#SENT} — nothing to say beyond the form itself.
     */
    static String message(EmailOtpService.SendResult result) {
        switch (result) {
            case COOLDOWN:
                return "pfOtpCooldown";
            case LIMIT_REACHED:
                return "pfOtpSendLimit";
            case NO_ADDRESS:
                return "pfOtpNoAddress";
            case MAIL_FAILED:
                return "pfOtpMailFailed";
            default:
                return null;
        }
    }

    static String message(EmailOtpService.VerifyResult result) {
        switch (result) {
            case INVALID:
                return "pfOtpInvalid";
            case EXPIRED:
                return "pfOtpExpired";
            case ATTEMPTS_EXHAUSTED:
                return "pfOtpExhausted";
            case NO_CODE:
                return "pfOtpNoCode";
            default:
                return null;
        }
    }
}
