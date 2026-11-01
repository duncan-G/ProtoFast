package dev.protofast.keycloak.ses;

import org.keycloak.Config;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.email.EmailSenderProviderFactory;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

import software.amazon.awssdk.http.urlconnection.UrlConnectionHttpClient;
import software.amazon.awssdk.services.sesv2.SesV2Client;

/**
 * SesEmailSenderProviderFactory is the email sender {@code ses}, which the image selects
 * with {@code KC_SPI_EMAIL_SENDER__PROVIDER=ses} before {@code kc.sh build}.
 *
 * <p>The SES client is built on the first send, not when Keycloak starts the factory, so
 * Keycloak starts where there's no AWS region, such as this image run outside an account.
 * It has the SDK's defaults: the region from {@code AWS_REGION} and the credentials chain,
 * which finds the ProtoStrap agent's container credentials
 * ({@code AWS_CONTAINER_CREDENTIALS_RELATIVE_URI}). Its HTTP client is the JDK's
 * {@code HttpURLConnection}, so the JAR carries no Netty or Apache client.
 */
public final class SesEmailSenderProviderFactory implements EmailSenderProviderFactory {

    public static final String ID = "ses";

    private final Object lock = new Object();
    private volatile SesV2Client client;

    @Override
    public EmailSenderProvider create(KeycloakSession session) {
        return new SesEmailSenderProvider(this::client);
    }

    /**
     * client is the one SES client, built the first time it's asked for. If building it
     * fails, as it does without a region, the next send tries again.
     */
    SesV2Client client() {
        SesV2Client existing = client;
        if (existing != null) {
            return existing;
        }
        synchronized (lock) {
            if (client == null) {
                client = SesV2Client.builder()
                        .httpClientBuilder(UrlConnectionHttpClient.builder())
                        .build();
            }
            return client;
        }
    }

    /** built says whether the SES client exists yet, for the tests. */
    boolean built() {
        return client != null;
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
        synchronized (lock) {
            if (client != null) {
                client.close();
                client = null;
            }
        }
    }

    @Override
    public String getId() {
        return ID;
    }
}
