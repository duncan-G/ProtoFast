package dev.protofast.keycloak.signup;

import jakarta.ws.rs.core.MultivaluedHashMap;
import jakarta.ws.rs.core.MultivaluedMap;

import org.jboss.logging.Logger;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.AuthenticationFlowException;
import org.keycloak.authentication.FormAction;
import org.keycloak.authentication.FormContext;
import org.keycloak.authentication.ValidationContext;
import org.keycloak.authentication.forms.RegistrationPage;
import org.keycloak.authentication.forms.RegistrationUserCreation;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.events.EventType;
import org.keycloak.forms.login.LoginFormsProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.protocol.oidc.OIDCLoginProtocol;
import org.keycloak.services.messages.Messages;
import org.keycloak.sessions.AuthenticationSessionModel;
import org.keycloak.userprofile.UserProfile;
import org.keycloak.userprofile.UserProfileContext;
import org.keycloak.userprofile.UserProfileProvider;

import static org.keycloak.services.managers.AuthenticationManager.NEW_USER_REGISTERED;

/**
 * Registration user creation that hands back an abandoned sign-up instead of refusing it.
 *
 * <p>Sign-up creates the account and then asks for a mailed code. Someone who closes the
 * tab at that screen leaves a shell behind — an account holding their address that nobody
 * has ever proved (see {@link SignupClaims}). Stock Keycloak then tells them, forever,
 * that the address is already registered: the one thing they cannot do with that address
 * is the thing they were trying to do. Signing in works, but "sign in" is not what
 * somebody who never finished signing up goes looking for.
 *
 * <p>So a sign-up that lands on a shell is treated as what it is — the same sign-up,
 * resumed. The row is reused rather than created, and the {@code verify-email-code}
 * required action mails the code exactly as it would for a first attempt. A stranger who
 * guesses someone else's abandoned address gets a mail they cannot read and reaches
 * nothing; the code is still the only way through, which is why this is safe.
 *
 * <p>Everything not a shell keeps the old behaviour, error message and all — the sign-up
 * form's "already registered" branch links to sign-in and stays the right answer there.
 *
 * <h2>Why a subclass</h2>
 * The whole duplicate check is a user-profile validator, and it compares against the user
 * the profile is <em>bound</em> to: a profile created for an existing user raises neither
 * EMAIL_EXISTS nor USERNAME_EXISTS for that user's own address. So the only override the
 * validation phase needs is {@link #getOrCreateUserProfile} — bind the profile to the
 * shell and the base class validates the form unchanged. The creation phase then updates
 * that user instead of calling {@code profile.create()}.
 */
public class ClaimingUserCreation extends RegistrationUserCreation {

    public static final String PROVIDER_ID = "protofast-registration-user-creation";

    private static final Logger LOG = Logger.getLogger(ClaimingUserCreation.class);

    /** The shell this sign-up resolved to, re-checked before anything is written to it. */
    private static final String NOTE_CLAIM = "pf.signup.claim";

    /** Request-scoped cache of the bound profile, mirroring what the base class does with its own. */
    private static final String ATTR_PROFILE = "pf.signup.profile";

    @Override
    public void validate(ValidationContext context) {
        AuthenticationSessionModel authSession = context.getAuthenticationSession();

        // Re-decided on every submission: the address can change between attempts within
        // one authentication session, and a stale note would bind the wrong account.
        authSession.removeAuthNote(NOTE_CLAIM);
        context.getSession().removeAttribute(ATTR_PROFILE);

        try {
            UserModel shell = findClaimable(context);
            if (shell != null) {
                authSession.setAuthNote(NOTE_CLAIM, shell.getId());
            }
        } catch (RuntimeException e) {
            // Never let this decide a registration by failing. Without the note the base
            // class runs exactly as it did before this class existed.
            LOG.warnf(e, "Could not evaluate the sign-up claim for realm %s", context.getRealm().getName());
            authSession.removeAuthNote(NOTE_CLAIM);
        }

        super.validate(context);
    }

    @Override
    public void buildPage(FormContext context, LoginFormsProvider form) {
        // Stock registration refuses to render once a user is on the session. Signing up
        // again with a shell is the same attempt resumed, so the form has to stay open.
        UserModel existing = context.getUser();
        if (existing != null && SignupClaims.isShell(context.getSession(), context.getRealm(), existing)) {
            return;
        }
        super.buildPage(context, form);
    }

    @Override
    public void success(FormContext context) {
        UserModel user = claimTarget(context);
        if (user == null) {
            super.success(context);
            return;
        }

        UserModel already = context.getUser();
        if (already != null && !already.getId().equals(user.getId())) {
            // Back-navigation into a half-finished flow for a *different* account.
            context.getEvent().detail(Details.EXISTING_USER, already.getUsername());
            throw new AuthenticationFlowException(AuthenticationFlowError.GENERIC_AUTHENTICATION_ERROR,
                    Errors.DIFFERENT_USER_AUTHENTICATING, Messages.EXPIRED_ACTION);
        }
        if (already != null) {
            // Same shell, submitted again. Stay on this attempt: the live code still
            // works, and a spent send budget must surface on the code screen, not as
            // "already registered".
            return;
        }

        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        String email = formData.getFirst(UserModel.EMAIL);
        String username = context.getRealm().isRegistrationEmailAsUsername()
                ? email
                : formData.getFirst(UserModel.USERNAME);

        // Writes back whatever the form carries beyond the address. Today that is nothing —
        // the form is email-only — but a field added to the user profile tomorrow should
        // follow the second attempt, not the abandoned first one.
        getOrCreateUserProfile(context, formData).update(false);

        context.getAuthenticationSession().setAuthNote(NEW_USER_REGISTERED, "true");
        context.setUser(user);
        context.getAuthenticationSession().setClientNote(OIDCLoginProtocol.LOGIN_HINT_PARAM, username);

        context.getEvent().detail(Details.USERNAME, username)
                .detail(Details.REGISTER_METHOD, "form")
                .detail(Details.EMAIL, email)
                .detail(Details.EXISTING_USER, user.getId());
        context.getEvent().user(user);
        context.getEvent().success();
        context.newEvent().event(EventType.LOGIN);
        context.getEvent().client(context.getAuthenticationSession().getClient().getClientId())
                .detail(Details.REDIRECT_URI, context.getAuthenticationSession().getRedirectUri())
                .detail(Details.AUTH_METHOD, context.getAuthenticationSession().getProtocol());
        String authType = context.getAuthenticationSession().getAuthNote(Details.AUTH_TYPE);
        if (authType != null) {
            context.getEvent().detail(Details.AUTH_TYPE, authType);
        }
    }

    /**
     * The base class caches one profile per request and both phases read it through here,
     * so binding it to the shell is enough to disarm the duplicate-address validators for
     * that one account — and only for it.
     */
    @Override
    public UserProfile getOrCreateUserProfile(FormContext context, MultivaluedMap<String, String> formData) {
        UserModel claimed = claimTarget(context);
        if (claimed == null) {
            return super.getOrCreateUserProfile(context, formData);
        }

        KeycloakSession session = context.getSession();
        UserProfile profile = (UserProfile) session.getAttribute(ATTR_PROFILE);
        if (profile == null) {
            profile = session.getProvider(UserProfileProvider.class)
                    .create(UserProfileContext.REGISTRATION, withoutSecrets(formData), claimed);
            session.setAttribute(ATTR_PROFILE, profile);
        }
        return profile;
    }

    /** The submitted address, if it belongs to a shell this sign-up is allowed to take over. */
    private UserModel findClaimable(ValidationContext context) {
        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        String email = formData.getFirst(UserModel.EMAIL);
        if (email == null || email.isBlank()) {
            return null;
        }

        String trimmed = email.trim();
        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();
        UserModel existing = session.users().getUserByEmail(realm, trimmed);
        if (existing == null) {
            existing = session.users().getUserByUsername(realm, trimmed);
        }
        return SignupClaims.isShell(session, realm, existing) ? existing : null;
    }

    /** Resolves the note, re-checking the account is still a shell before it is written to. */
    private UserModel claimTarget(FormContext context) {
        String id = context.getAuthenticationSession().getAuthNote(NOTE_CLAIM);
        if (id == null) {
            return null;
        }

        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();
        UserModel user = session.users().getUserById(realm, id);
        return SignupClaims.isShell(session, realm, user) ? user : null;
    }

    /** The base class strips these before building a profile; a bound profile needs the same. */
    private static MultivaluedMap<String, String> withoutSecrets(MultivaluedMap<String, String> formData) {
        MultivaluedHashMap<String, String> copy = new MultivaluedHashMap<>(formData);
        copy.remove(RegistrationPage.FIELD_RECAPTCHA_RESPONSE);
        copy.remove(RegistrationPage.FIELD_PASSWORD);
        copy.remove(RegistrationPage.FIELD_PASSWORD_CONFIRM);
        return copy;
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Registration User Creation with Claim";
    }

    @Override
    public String getHelpText() {
        return "Creates the user from the registration form, or resumes an unverified sign-up "
                + "that already holds the address instead of refusing it as a duplicate.";
    }

    @Override
    public FormAction create(KeycloakSession session) {
        return this;
    }
}
