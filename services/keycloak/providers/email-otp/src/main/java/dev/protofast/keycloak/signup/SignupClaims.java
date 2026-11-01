package dev.protofast.keycloak.signup;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/**
 * Decides whether an account that already holds an address may be handed back to
 * whoever is signing up with it again.
 *
 * <p>An account that abandoned sign-up at the code screen is a <em>shell</em>: it holds
 * an address nobody has ever proved. That is not a judgement call in this realm, it is an
 * invariant — every way into an account ends in a mailed code or a passkey, and the code
 * path sets {@code emailVerified}. So an unverified account with no credential and no
 * broker link has never been signed into by anyone, has no session, no local user row and
 * nothing to steal. Handing it back to the next person who types that address costs an
 * attacker nothing they did not already have: they still cannot get past the code, which
 * only the address owner reads. A shell stays reclaimable from sign-up until someone
 * proves the address; there is no wait and no retry cap.
 *
 * <p>Everything else — verified, holds a passkey, linked to Apple, federated, disabled —
 * belongs to somebody. Registration refuses those the way it always did, and the sign-up
 * form points at sign-in instead.
 */
public final class SignupClaims {

    /** Left on older shells when claims were counted; stripped on verify so they do not linger. */
    static final String ATTR_COUNT = "pf.signup.claims";
    static final String ATTR_LAST_AT = "pf.signup.claimedAt";

    private SignupClaims() {
    }

    /** Has this account never been proved by anybody? */
    static boolean isShell(KeycloakSession session, RealmModel realm, UserModel user) {
        return user != null
                && user.getId() != null
                && user.isEnabled()
                && !user.isEmailVerified()
                && user.getServiceAccountClientLink() == null
                && user.getFederationLink() == null
                && user.credentialManager().getStoredCredentialsStream().findAny().isEmpty()
                && session.users().getFederatedIdentitiesStream(realm, user).findAny().isEmpty();
    }

    /**
     * Called the moment the address is proved: leftover claim counters from older
     * builds have nothing left to limit and would only sit on the user forever.
     */
    public static void clear(UserModel user) {
        user.removeAttribute(ATTR_COUNT);
        user.removeAttribute(ATTR_LAST_AT);
    }
}
