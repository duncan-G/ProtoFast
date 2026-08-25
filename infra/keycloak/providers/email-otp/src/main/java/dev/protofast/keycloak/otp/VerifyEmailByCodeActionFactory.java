package dev.protofast.keycloak.otp;

import org.keycloak.Config;
import org.keycloak.authentication.RequiredActionFactory;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

public class VerifyEmailByCodeActionFactory implements RequiredActionFactory {

    /** The realm registers this as its default action in place of VERIFY_EMAIL. */
    public static final String PROVIDER_ID = "verify-email-code";

    private static final VerifyEmailByCodeAction INSTANCE = new VerifyEmailByCodeAction();

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayText() {
        return "Verify Email by Code";
    }

    @Override
    public RequiredActionProvider create(KeycloakSession session) {
        return INSTANCE;
    }

    @Override
    public boolean isOneTimeAction() {
        return true;
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
