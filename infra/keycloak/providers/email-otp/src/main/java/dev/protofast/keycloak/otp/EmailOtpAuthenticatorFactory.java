package dev.protofast.keycloak.otp;

import java.util.Collections;
import java.util.List;

import java.util.Set;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.credential.OTPCredentialModel;
import org.keycloak.provider.ProviderConfigProperty;

public class EmailOtpAuthenticatorFactory implements AuthenticatorFactory {

    public static final String PROVIDER_ID = "email-otp";

    /**
     * The realm's brute-force protector silently drops failures from any category outside
     * its own allow-list (password, otp, recovery codes), so a bespoke name here would
     * mean wrong codes cost nothing at all. Reporting as "otp" is what makes guessing a
     * mailed code count towards the realm's lockout — and, now that no password remains,
     * makes the realm's failure factor an OTP setting in practice.
     */
    static final Set<String> BRUTE_FORCE_CATEGORIES = Set.of(OTPCredentialModel.TYPE);

    private static final EmailOtpAuthenticator INSTANCE = new EmailOtpAuthenticator();

    /**
     * ALTERNATIVE only. REQUIRED would put a mailed code in front of a passkey holder
     * on every sign-in; DISABLED and CONDITIONAL have no meaning for a step that is
     * available to everyone.
     */
    private static final AuthenticationExecutionModel.Requirement[] REQUIREMENTS = {
            AuthenticationExecutionModel.Requirement.REQUIRED,
            AuthenticationExecutionModel.Requirement.ALTERNATIVE,
            AuthenticationExecutionModel.Requirement.DISABLED,
    };

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    /** Also the label on the "Try another way" chooser. */
    @Override
    public String getDisplayType() {
        return "Email Code";
    }

    @Override
    public String getReferenceCategory() {
        return OTPCredentialModel.TYPE;
    }

    @Override
    public String getHelpText() {
        return "Sends a single-use numeric code to the account's email address and asks for it back.";
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public AuthenticationExecutionModel.Requirement[] getRequirementChoices() {
        return REQUIREMENTS;
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return Collections.emptyList();
    }

    @Override
    public Authenticator create(KeycloakSession session) {
        return INSTANCE;
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }
}
