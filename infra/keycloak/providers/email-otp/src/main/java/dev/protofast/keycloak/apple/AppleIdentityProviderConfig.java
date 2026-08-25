package dev.protofast.keycloak.apple;

import org.keycloak.broker.oidc.OIDCIdentityProviderConfig;
import org.keycloak.models.IdentityProviderModel;

/**
 * Apple's three extra pieces of enrolment. The "client secret" Apple expects is not a
 * shared string but a JWT the relying party signs for itself, so what is configured
 * here is the signing material rather than a secret: the team that owns the app, the
 * id of the key, and the key itself.
 */
public class AppleIdentityProviderConfig extends OIDCIdentityProviderConfig {

    public static final String TEAM_ID = "teamId";
    public static final String KEY_ID = "keyId";
    public static final String PRIVATE_KEY = "p8PrivateKey";

    public AppleIdentityProviderConfig() {
        super();
    }

    public AppleIdentityProviderConfig(IdentityProviderModel model) {
        super(model);
    }

    /** Apple Developer team identifier — the `iss` of the signed secret. */
    public String getTeamId() {
        return getConfig().get(TEAM_ID);
    }

    public void setTeamId(String teamId) {
        getConfig().put(TEAM_ID, teamId);
    }

    /** Identifier of the Sign in with Apple key — the `kid` header of the signed secret. */
    public String getKeyId() {
        return getConfig().get(KEY_ID);
    }

    public void setKeyId(String keyId) {
        getConfig().put(KEY_ID, keyId);
    }

    /** Contents of the downloaded .p8 file: a PKCS#8 EC private key, PEM armour optional. */
    public String getPrivateKey() {
        return getConfig().get(PRIVATE_KEY);
    }

    public void setPrivateKey(String privateKey) {
        getConfig().put(PRIVATE_KEY, privateKey);
    }
}
