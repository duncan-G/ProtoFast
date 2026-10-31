package dev.protofast.keycloak.apple;

import java.util.List;

import org.keycloak.broker.provider.AbstractIdentityProviderFactory;
import org.keycloak.broker.social.SocialIdentityProviderFactory;
import org.keycloak.models.IdentityProviderModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.provider.ProviderConfigurationBuilder;

public class AppleIdentityProviderFactory extends AbstractIdentityProviderFactory<AppleIdentityProvider>
        implements SocialIdentityProviderFactory<AppleIdentityProvider> {

    public static final String PROVIDER_ID = "apple";

    @Override
    public String getName() {
        return "Apple";
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public AppleIdentityProvider create(KeycloakSession session, IdentityProviderModel model) {
        return new AppleIdentityProvider(session, new AppleIdentityProviderConfig(model));
    }

    @Override
    public AppleIdentityProviderConfig createConfig() {
        return new AppleIdentityProviderConfig();
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return ProviderConfigurationBuilder.create()
                .property()
                .name(AppleIdentityProviderConfig.TEAM_ID)
                .label("Team ID")
                .helpText("Apple Developer team identifier that owns the Services ID.")
                .type(ProviderConfigProperty.STRING_TYPE)
                .add()
                .property()
                .name(AppleIdentityProviderConfig.KEY_ID)
                .label("Key ID")
                .helpText("Identifier of the Sign in with Apple key the .p8 file belongs to.")
                .type(ProviderConfigProperty.STRING_TYPE)
                .add()
                .property()
                .name(AppleIdentityProviderConfig.PRIVATE_KEY)
                .label("Private key (.p8)")
                .helpText("Contents of the downloaded .p8 key. Used to sign a fresh client secret "
                        + "on every token request, so there is nothing to rotate.")
                .type(ProviderConfigProperty.TEXT_TYPE)
                .secret(true)
                .add()
                .build();
    }
}
