package dev.protofast.keycloak.otp;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.util.Base64;
import java.util.HashMap;
import java.util.Map;

import org.jboss.logging.Logger;
import org.keycloak.common.util.Time;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.sessions.AuthenticationSessionModel;

/**
 * Issues, mails and checks the six-digit codes that stand in for a password.
 *
 * <p>The code itself is never stored. What lives on the user is a SHA-256 digest
 * salted with the realm and user ids, so a second sign-up in a new tab can still
 * accept the mail that is already on the way. A digest is worthless without the
 * six digits, and it is dropped once the code is used, expires, or is spent.
 *
 * <p>The cooldown is stamped on the user too: a fresh OIDC request is one click
 * away and must not mint another mail. The per-tab send cap still lives on the
 * authentication session, because that is the resend button, not a new attempt.
 * The realm's brute-force protector is a separate, longer-lived control that the
 * callers feed on every wrong code.
 */
final class EmailOtpService {

    /** Six digits, numeric — typed on a phone keyboard, read out loud without spelling. */
    static final int CODE_DIGITS = 6;

    static final int LIFETIME_SECONDS = 600;

    /** Wrong codes tolerated per issued code. Past this the code is destroyed, not merely refused. */
    static final int MAX_ATTEMPTS = 5;

    /** Codes mailed per authentication session, including the first one. */
    static final int MAX_SENDS = 3;

    static final int RESEND_COOLDOWN_SECONDS = 60;

    private static final Logger LOG = Logger.getLogger(EmailOtpService.class);
    private static final SecureRandom RANDOM = new SecureRandom();

    private static final String NOTE_SENDS = "pf.otp.sends";
    private static final String NOTE_SENT_AT = "pf.otp.sentAt";

    /** Live code and throttle: survive a new authentication session. */
    private static final String ATTR_DIGEST = "pf.otp.digest";
    private static final String ATTR_EXPIRES_AT = "pf.otp.expiresAt";
    private static final String ATTR_ATTEMPTS = "pf.otp.attempts";
    private static final String ATTR_SENT_AT = "pf.otp.sentAt";

    enum SendResult {
        SENT,
        /** Asked again inside the cooldown — the previous code is still the live one. */
        COOLDOWN,
        /** This session has had all the codes it is going to get. */
        LIMIT_REACHED,
        NO_ADDRESS,
        MAIL_FAILED,
    }

    enum VerifyResult {
        VALID,
        INVALID,
        EXPIRED,
        /** Nothing to check against: never issued, or already spent. */
        NO_CODE,
        /** The attempt budget is gone and the code with it; only a fresh one can succeed. */
        ATTEMPTS_EXHAUSTED,
    }

    private final KeycloakSession session;
    private final RealmModel realm;
    private final AuthenticationSessionModel authSession;

    EmailOtpService(KeycloakSession session, RealmModel realm, AuthenticationSessionModel authSession) {
        this.session = session;
        this.realm = realm;
        this.authSession = authSession;
    }

    /**
     * Mails a fresh code, subject to the cooldown and the per-session send limit.
     * The stored digest is replaced only after the mail is accepted for delivery, so a
     * failed send leaves the previous code working instead of silently invalidating it.
     */
    SendResult send(UserModel user, String subjectKey, String template) {
        String address = user.getEmail();
        if (address == null || address.isBlank()) {
            return SendResult.NO_ADDRESS;
        }

        int now = Time.currentTime();
        if (secondsUntilResend(user) > 0) {
            return SendResult.COOLDOWN;
        }
        int sends = note(NOTE_SENDS, 0);
        if (sends >= MAX_SENDS) {
            return SendResult.LIMIT_REACHED;
        }

        String code = generate();
        Map<String, Object> attributes = new HashMap<>();
        attributes.put("code", code);
        attributes.put("codeLifetimeMinutes", LIFETIME_SECONDS / 60);

        try {
            session.getProvider(EmailTemplateProvider.class)
                    .setRealm(realm)
                    .setUser(user)
                    .setAuthenticationSession(authSession)
                    .send(subjectKey, template, attributes);
        } catch (EmailException e) {
            // The caller turns this into a visible error on the form. A silent
            // "check your inbox" for mail that was never accepted is a dead end.
            LOG.errorf(e, "Could not send the sign-in code for realm %s", realm.getName());
            return SendResult.MAIL_FAILED;
        }

        user.setSingleAttribute(ATTR_DIGEST, digest(user, code));
        user.setSingleAttribute(ATTR_EXPIRES_AT, String.valueOf(now + LIFETIME_SECONDS));
        user.setSingleAttribute(ATTR_ATTEMPTS, "0");
        user.setSingleAttribute(ATTR_SENT_AT, String.valueOf(now));
        authSession.setAuthNote(NOTE_SENDS, String.valueOf(sends + 1));
        authSession.setAuthNote(NOTE_SENT_AT, String.valueOf(now));
        return SendResult.SENT;
    }

    VerifyResult verify(UserModel user, String input) {
        String expected = user.getFirstAttribute(ATTR_DIGEST);
        if (expected == null) {
            return VerifyResult.NO_CODE;
        }
        if (Time.currentTime() > attribute(user, ATTR_EXPIRES_AT)) {
            discard(user);
            return VerifyResult.EXPIRED;
        }

        // Count the attempt before checking it, so an abandoned request still spends
        // its budget and a client that never reads the response gains nothing.
        int attempts = attribute(user, ATTR_ATTEMPTS) + 1;
        user.setSingleAttribute(ATTR_ATTEMPTS, String.valueOf(attempts));

        String candidate = input == null ? "" : input.replaceAll("\\s", "");
        if (MessageDigest.isEqual(
                expected.getBytes(StandardCharsets.UTF_8),
                digest(user, candidate).getBytes(StandardCharsets.UTF_8))) {
            discard(user);
            return VerifyResult.VALID;
        }

        if (attempts >= MAX_ATTEMPTS) {
            discard(user);
            return VerifyResult.ATTEMPTS_EXHAUSTED;
        }
        return VerifyResult.INVALID;
    }

    /** Seconds left on the resend cooldown; 0 when another code can be asked for now. */
    int secondsUntilResend(UserModel user) {
        int now = Time.currentTime();
        int remaining = 0;
        if (note(NOTE_SENDS, 0) > 0) {
            remaining = note(NOTE_SENT_AT, 0) + RESEND_COOLDOWN_SECONDS - now;
        }
        int lastOnUser = attribute(user, ATTR_SENT_AT);
        if (lastOnUser > 0) {
            remaining = Math.max(remaining, lastOnUser + RESEND_COOLDOWN_SECONDS - now);
        }
        return Math.max(remaining, 0);
    }

    boolean resendAllowed(UserModel user) {
        return note(NOTE_SENDS, 0) < MAX_SENDS && secondsUntilResend(user) == 0;
    }

    /** Is there a code out there that could still be typed in successfully? */
    boolean hasLiveCode(UserModel user) {
        return user.getFirstAttribute(ATTR_DIGEST) != null
                && Time.currentTime() <= attribute(user, ATTR_EXPIRES_AT);
    }

    /** Drop the mailed-code state once the address is proved. */
    static void clearThrottle(UserModel user) {
        user.removeAttribute(ATTR_DIGEST);
        user.removeAttribute(ATTR_EXPIRES_AT);
        user.removeAttribute(ATTR_ATTEMPTS);
        user.removeAttribute(ATTR_SENT_AT);
    }

    private void discard(UserModel user) {
        user.removeAttribute(ATTR_DIGEST);
        user.removeAttribute(ATTR_EXPIRES_AT);
        user.removeAttribute(ATTR_ATTEMPTS);
    }

    private static int attribute(UserModel user, String name) {
        if (user == null) {
            return 0;
        }
        String raw = user.getFirstAttribute(name);
        if (raw == null) {
            return 0;
        }
        try {
            return Integer.parseInt(raw);
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    private static String generate() {
        int max = (int) Math.pow(10, CODE_DIGITS);
        return String.format("%0" + CODE_DIGITS + "d", RANDOM.nextInt(max));
    }

    /**
     * Salted with the realm and the user, not the browser tab: signing up again
     * must still be able to type the code that is already in the inbox.
     */
    private String digest(UserModel user, String code) {
        try {
            MessageDigest sha = MessageDigest.getInstance("SHA-256");
            sha.update(realm.getId().getBytes(StandardCharsets.UTF_8));
            sha.update((byte) 0);
            sha.update(user.getId().getBytes(StandardCharsets.UTF_8));
            sha.update((byte) 0);
            sha.update(code.getBytes(StandardCharsets.UTF_8));
            return Base64.getEncoder().encodeToString(sha.digest());
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is required by the Java platform", e);
        }
    }

    private int note(String name, int fallback) {
        String raw = authSession.getAuthNote(name);
        if (raw == null) {
            return fallback;
        }
        try {
            return Integer.parseInt(raw);
        } catch (NumberFormatException e) {
            return fallback;
        }
    }
}
