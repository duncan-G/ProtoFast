package dev.protofast.keycloak.apple;

import java.io.IOException;
import java.security.KeyFactory;
import java.security.PrivateKey;
import java.security.spec.PKCS8EncodedKeySpec;
import java.util.Base64;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.FormParam;
import jakarta.ws.rs.POST;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import jakarta.ws.rs.core.UriBuilder;

import com.fasterxml.jackson.databind.JsonNode;

import org.jboss.logging.Logger;
import org.keycloak.broker.oidc.OIDCIdentityProvider;
import org.keycloak.broker.provider.BrokeredIdentityContext;
import org.keycloak.broker.provider.IdentityBrokerException;
import org.keycloak.broker.provider.AuthenticationRequest;
import org.keycloak.broker.provider.UserAuthenticationIdentityProvider;
import org.keycloak.broker.social.SocialIdentityProvider;
import org.keycloak.common.util.Time;
import org.keycloak.crypto.Algorithm;
import org.keycloak.crypto.AsymmetricSignatureSignerContext;
import org.keycloak.crypto.KeyStatus;
import org.keycloak.crypto.KeyType;
import org.keycloak.crypto.KeyUse;
import org.keycloak.crypto.KeyWrapper;
import org.keycloak.events.EventBuilder;
import org.keycloak.jose.jws.JWSBuilder;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.representations.AccessTokenResponse;
import org.keycloak.representations.JsonWebToken;
import org.keycloak.util.JsonSerialization;

/**
 * Sign in with Apple.
 *
 * <p>Apple is OIDC except in three places, and each of them is why this exists instead
 * of a generic OIDC provider:
 *
 * <ul>
 *   <li><b>The client secret is a JWT the relying party signs</b>, capped at six months.
 *       A static one configured by hand expires and takes Apple sign-in down with it, so
 *       it is minted fresh on every token request from the .p8 key and never stored.</li>
 *   <li><b>The response comes back as a form POST</b>, not a redirect with query
 *       parameters, because the requested scopes include the user's name.</li>
 *   <li><b>Name and email arrive exactly once</b> — in that POST, on the very first
 *       authorization, and never again. Missing them means the account is created
 *       without a name for good, so the payload is read before the redirect is
 *       processed and folded into the brokered identity.</li>
 * </ul>
 *
 * <p>Note that a user who chose <b>Hide My Email</b> arrives as a per-app
 * {@code @privaterelay.appleid.com} address. That is genuinely a different address from
 * the one on any existing account, so it links to nothing and creates a new account.
 */
public class AppleIdentityProvider extends OIDCIdentityProvider
        implements SocialIdentityProvider<org.keycloak.broker.oidc.OIDCIdentityProviderConfig> {

    private static final Logger LOG = Logger.getLogger(AppleIdentityProvider.class);

    private static final String ISSUER = "https://appleid.apple.com";
    private static final String AUTH_URL = ISSUER + "/auth/authorize";
    private static final String TOKEN_URL = ISSUER + "/auth/token";
    private static final String JWKS_URL = ISSUER + "/auth/keys";
    private static final String DEFAULT_SCOPE = "openid name email";

    /** Apple's hard ceiling on a client secret is six months; stay well inside it. */
    private static final int SECRET_LIFETIME_SECONDS = 60 * 60 * 24 * 30;

    /**
     * The one-shot profile from the authorization POST, held for the length of this
     * request. Provider instances are created per request, so there is nothing to leak
     * between users.
     */
    private String firstAuthorizationProfile;

    public AppleIdentityProvider(KeycloakSession session, AppleIdentityProviderConfig config) {
        super(session, config);
        config.setAuthorizationUrl(AUTH_URL);
        config.setTokenUrl(TOKEN_URL);
        config.setJwksUrl(JWKS_URL);
        config.setIssuer(ISSUER);
        config.setUseJwksUrl(true);
        config.setValidateSignature(true);
        // Apple has no userinfo endpoint; everything it will say is in the id token.
        config.setDisableUserInfoService(true);
    }

    @Override
    protected String getDefaultScopes() {
        return DEFAULT_SCOPE;
    }

    @Override
    protected UriBuilder createAuthorizationUrl(AuthenticationRequest request) {
        // form_post is mandatory once the scope asks for name or email; Apple rejects
        // the request outright with the default query response mode.
        return super.createAuthorizationUrl(request).queryParam("response_mode", "form_post");
    }

    /**
     * Mints the secret immediately before it is used. Nothing to rotate, and nothing on
     * disk that can quietly go stale.
     */
    @Override
    public org.keycloak.http.simple.SimpleHttpRequest authenticateTokenRequest(
            org.keycloak.http.simple.SimpleHttpRequest tokenRequest) {
        getConfig().setClientSecret(createClientSecret());
        return super.authenticateTokenRequest(tokenRequest);
    }

    @Override
    public Object callback(RealmModel realm, UserAuthenticationIdentityProvider.AuthenticationCallback callback, EventBuilder event) {
        return new AppleEndpoint(callback, realm, event, this);
    }

    @Override
    protected BrokeredIdentityContext extractIdentity(AccessTokenResponse tokenResponse, String accessToken, JsonWebToken idToken)
            throws IOException {
        BrokeredIdentityContext identity = super.extractIdentity(tokenResponse, accessToken, idToken);
        applyFirstAuthorizationProfile(identity);
        return identity;
    }

    void rememberFirstAuthorizationProfile(String rawUserPayload) {
        this.firstAuthorizationProfile = rawUserPayload;
    }

    /**
     * Folds Apple's one-and-only-once {@code user} payload into the identity. Absent on
     * every authorization after the first, which is not an error — an existing link
     * already carries the name.
     */
    private void applyFirstAuthorizationProfile(BrokeredIdentityContext identity) {
        if (firstAuthorizationProfile == null || firstAuthorizationProfile.isBlank()) {
            return;
        }
        try {
            JsonNode payload = JsonSerialization.readValue(firstAuthorizationProfile, JsonNode.class);
            JsonNode name = payload.get("name");
            if (name != null) {
                if (name.hasNonNull("firstName")) {
                    identity.setFirstName(name.get("firstName").asText());
                }
                if (name.hasNonNull("lastName")) {
                    identity.setLastName(name.get("lastName").asText());
                }
            }
            if (identity.getEmail() == null && payload.hasNonNull("email")) {
                identity.setEmail(payload.get("email").asText());
            }
        } catch (IOException e) {
            // A name is nice to have; failing the whole sign-in over it is not.
            LOG.warnf(e, "Could not read the Apple authorization profile payload");
        }
    }

    private String createClientSecret() {
        AppleIdentityProviderConfig config = (AppleIdentityProviderConfig) getConfig();
        long now = Time.currentTime();

        JsonWebToken token = new JsonWebToken();
        token.issuer(config.getTeamId());
        token.subject(config.getClientId());
        token.audience(ISSUER);
        token.iat(now);
        token.exp(now + SECRET_LIFETIME_SECONDS);
        token.id(org.keycloak.models.utils.KeycloakModelUtils.generateId());

        return new JWSBuilder()
                .kid(config.getKeyId())
                .type("JWT")
                .jsonContent(token)
                .sign(new AsymmetricSignatureSignerContext(signingKey(config)));
    }

    private static KeyWrapper signingKey(AppleIdentityProviderConfig config) {
        String pem = config.getPrivateKey();
        if (pem == null || pem.isBlank()) {
            throw new IdentityBrokerException("The Apple identity provider has no signing key configured");
        }

        String base64 = pem.replaceAll("-----(BEGIN|END)[^-]*-----", "").replaceAll("\\s", "");
        PrivateKey privateKey;
        try {
            privateKey = KeyFactory.getInstance("EC")
                    .generatePrivate(new PKCS8EncodedKeySpec(Base64.getDecoder().decode(base64)));
        } catch (Exception e) {
            throw new IdentityBrokerException("The Apple signing key is not a PKCS#8 EC private key", e);
        }

        KeyWrapper key = new KeyWrapper();
        key.setKid(config.getKeyId());
        key.setAlgorithm(Algorithm.ES256);
        key.setType(KeyType.EC);
        key.setUse(KeyUse.SIG);
        key.setStatus(KeyStatus.ACTIVE);
        key.setPrivateKey(privateKey);
        return key;
    }

    /**
     * Adds the form-POST arm of the redirect. The inherited GET handler still exists and
     * still works — Apple uses it when the scope asks for nothing but {@code openid}.
     */
    protected static class AppleEndpoint extends OIDCIdentityProvider.OIDCEndpoint {

        private final AppleIdentityProvider provider;

        AppleEndpoint(UserAuthenticationIdentityProvider.AuthenticationCallback callback,
                      RealmModel realm,
                      EventBuilder event,
                      AppleIdentityProvider provider) {
            super(callback, realm, event, provider);
            this.provider = provider;
        }

        @POST
        @Consumes(MediaType.APPLICATION_FORM_URLENCODED)
        public Response authResponseFormPost(@FormParam("state") String state,
                                             @FormParam("code") String authorizationCode,
                                             @FormParam("error") String error,
                                             @FormParam("user") String user) {
            // Read before the exchange: extractIdentity runs inside authResponse and this
            // payload is the only place the name will ever appear.
            provider.rememberFirstAuthorizationProfile(user);
            return authResponse(state, authorizationCode, error, null);
        }
    }
}
