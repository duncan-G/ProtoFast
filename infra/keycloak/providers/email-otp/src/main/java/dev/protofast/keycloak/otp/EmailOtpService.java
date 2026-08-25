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
 * <p>The code itself is never stored. What lives in the authentication session is a
 * SHA-256 digest salted with that session's own id and tab id, so a digest lifted out
 * of one session cannot be replayed in another — and a code that leaks after the fact
 * is worth nothing once the session is gone.
 *
 * <p>Every counter (attempts, resends, cooldown) is a note on the same authentication
 * session, which means it dies with the session and cannot be reset by starting the
 * step again. The realm's brute-force protector is a separate, longer-lived control
 * that the callers feed on every wrong code.
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

    private static final String NOTE_DIGEST = "pf.otp.digest";
    private static final String NOTE_EXPIRES_AT = "pf.otp.expiresAt";
    private static final String NOTE_ATTEMPTS = "pf.otp.attempts";
    private static final String NOTE_SENDS = "pf.otp.sends";
    private static final String NOTE_SENT_AT = "pf.otp.sentAt";

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
        int sends = note(NOTE_SENDS, 0);
        if (sends > 0 && now - note(NOTE_SENT_AT, 0) < RESEND_COOLDOWN_SECONDS) {
            return SendResult.COOLDOWN;
        }
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

        authSession.setAuthNote(NOTE_DIGEST, digest(code));
        authSession.setAuthNote(NOTE_EXPIRES_AT, String.valueOf(now + LIFETIME_SECONDS));
        authSession.setAuthNote(NOTE_ATTEMPTS, "0");
        authSession.setAuthNote(NOTE_SENDS, String.valueOf(sends + 1));
        authSession.setAuthNote(NOTE_SENT_AT, String.valueOf(now));
        return SendResult.SENT;
    }

    VerifyResult verify(String input) {
        String expected = authSession.getAuthNote(NOTE_DIGEST);
        if (expected == null) {
            return VerifyResult.NO_CODE;
        }
        if (Time.currentTime() > note(NOTE_EXPIRES_AT, 0)) {
            discard();
            return VerifyResult.EXPIRED;
        }

        // Count the attempt before checking it, so an abandoned request still spends
        // its budget and a client that never reads the response gains nothing.
        int attempts = note(NOTE_ATTEMPTS, 0) + 1;
        authSession.setAuthNote(NOTE_ATTEMPTS, String.valueOf(attempts));

        String candidate = input == null ? "" : input.replaceAll("\\s", "");
        if (MessageDigest.isEqual(
                expected.getBytes(StandardCharsets.UTF_8),
                digest(candidate).getBytes(StandardCharsets.UTF_8))) {
            discard(); // single use
            return VerifyResult.VALID;
        }

        if (attempts >= MAX_ATTEMPTS) {
            discard();
            return VerifyResult.ATTEMPTS_EXHAUSTED;
        }
        return VerifyResult.INVALID;
    }

    /** Seconds left on the resend cooldown; 0 when another code can be asked for now. */
    int secondsUntilResend() {
        if (note(NOTE_SENDS, 0) == 0) {
            return 0;
        }
        int remaining = note(NOTE_SENT_AT, 0) + RESEND_COOLDOWN_SECONDS - Time.currentTime();
        return Math.max(remaining, 0);
    }

    boolean resendAllowed() {
        return note(NOTE_SENDS, 0) < MAX_SENDS && secondsUntilResend() == 0;
    }

    /** True once a code has been mailed in this session — the form is a re-render, not a first visit. */
    boolean issued() {
        return note(NOTE_SENDS, 0) > 0;
    }

    /** Is there a code out there that could still be typed in successfully? */
    boolean hasLiveCode() {
        return authSession.getAuthNote(NOTE_DIGEST) != null && Time.currentTime() <= note(NOTE_EXPIRES_AT, 0);
    }

    /** Forgets everything about this session's codes, so the next visit starts from zero. */
    void reset() {
        discard();
        authSession.removeAuthNote(NOTE_SENDS);
        authSession.removeAuthNote(NOTE_SENT_AT);
    }

    private void discard() {
        authSession.removeAuthNote(NOTE_DIGEST);
        authSession.removeAuthNote(NOTE_EXPIRES_AT);
        authSession.removeAuthNote(NOTE_ATTEMPTS);
    }

    private static String generate() {
        int max = (int) Math.pow(10, CODE_DIGITS);
        return String.format("%0" + CODE_DIGITS + "d", RANDOM.nextInt(max));
    }

    /**
     * Salted with the parent session id and the tab id: the digest is meaningful only
     * inside the exact browser tab that asked for the code.
     */
    private String digest(String code) {
        try {
            MessageDigest sha = MessageDigest.getInstance("SHA-256");
            sha.update(authSession.getParentSession().getId().getBytes(StandardCharsets.UTF_8));
            sha.update((byte) 0);
            sha.update(authSession.getTabId().getBytes(StandardCharsets.UTF_8));
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
