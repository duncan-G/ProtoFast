package dev.protofast.keycloak.ses;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.ServiceLoader;

import org.junit.jupiter.api.Test;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailSenderProviderFactory;
import org.keycloak.models.UserModel;

import software.amazon.awssdk.awscore.exception.AwsErrorDetails;
import software.amazon.awssdk.core.exception.SdkClientException;
import software.amazon.awssdk.core.exception.SdkException;
import software.amazon.awssdk.services.sesv2.SesV2Client;
import software.amazon.awssdk.services.sesv2.model.SendEmailRequest;
import software.amazon.awssdk.services.sesv2.model.SendEmailResponse;
import software.amazon.awssdk.services.sesv2.model.SesV2Exception;

/** The provider and its factory, with SES replaced by a fake client. */
class SesEmailSenderProviderTest {

    private static final Map<String, String> REALM = Map.of("from", "no-reply@protofast.example.com");

    /** FakeSes records each request and answers with a message id, or throws. */
    private static final class FakeSes implements SesV2Client {
        final List<SendEmailRequest> requests = new ArrayList<>();
        SdkException failure;

        @Override
        public SendEmailResponse sendEmail(SendEmailRequest request) {
            requests.add(request);
            if (failure != null) {
                throw failure;
            }
            return SendEmailResponse.builder().messageId("0100-test-message-id").build();
        }

        @Override
        public String serviceName() {
            return SERVICE_NAME;
        }

        @Override
        public void close() {
        }
    }

    @Test
    void sendSendsTheBuiltRequestWithTheClient() throws EmailException {
        FakeSes ses = new FakeSes();

        new SesEmailSenderProvider(() -> ses).send(REALM, "someone@example.com", "Subject", "Text", "<p>Html</p>");

        assertEquals(1, ses.requests.size());
        SendEmailRequest request = ses.requests.get(0);
        assertEquals("no-reply@protofast.example.com", request.fromEmailAddress());
        assertEquals(List.of("someone@example.com"), request.destination().toAddresses());
        assertEquals("Subject", request.content().simple().subject().data());
    }

    @Test
    void anInvalidMessageNeverReachesSes() {
        FakeSes ses = new FakeSes();

        assertThrows(EmailException.class,
                () -> new SesEmailSenderProvider(() -> ses).send(Map.of(), "someone@example.com", "s", "t", "h"));
        assertTrue(ses.requests.isEmpty());
    }

    @Test
    void aRefusalNamesSesErrorButNotTheRecipient() {
        FakeSes ses = new FakeSes();
        ses.failure = SesV2Exception.builder()
                .awsErrorDetails(AwsErrorDetails.builder()
                        .errorCode("MessageRejected")
                        .errorMessage("Email address is not verified. The following identities failed the check in region US-EAST-1: someone@example.com")
                        .build())
                .statusCode(400)
                .requestId("req-1")
                .build();

        EmailException e = assertThrows(EmailException.class,
                () -> new SesEmailSenderProvider(() -> ses).send(REALM, "someone@example.com", "s", "t", "h"));

        assertTrue(e.getMessage().contains("MessageRejected (HTTP 400, request id req-1)"), e.getMessage());
        assertTrue(e.getMessage().contains("Email address is not verified"), e.getMessage());
        assertFalse(e.getMessage().contains("someone@example.com"), e.getMessage());
        // Keycloak logs a cause's message too, so there is none.
        assertNull(e.getCause());
    }

    @Test
    void aClientFailureIsAnEmailException() {
        FakeSes ses = new FakeSes();
        ses.failure = SdkClientException.create("Unable to load region from any of the providers in the chain");

        EmailException e = assertThrows(EmailException.class,
                () -> new SesEmailSenderProvider(() -> ses).send(REALM, "someone@example.com", "s", "t", "h"));

        assertTrue(e.getMessage().contains("SdkClientException: Unable to load region"), e.getMessage());
    }

    @Test
    void failingToBuildTheClientIsAnEmailException() {
        SesEmailSenderProvider provider = new SesEmailSenderProvider(() -> {
            throw SdkClientException.create("Unable to load credentials from any of the providers in the chain");
        });

        EmailException e = assertThrows(EmailException.class,
                () -> provider.send(REALM, "someone@example.com", "s", "t", "h"));

        assertTrue(e.getMessage().contains("Unable to load credentials"), e.getMessage());
    }

    @Test
    void sendingToAUserWithoutAnAddressIsRefused() {
        FakeSes ses = new FakeSes();

        assertThrows(EmailException.class,
                () -> new SesEmailSenderProvider(() -> ses).send(REALM, (UserModel) null, "s", "t", "h"));
        assertTrue(ses.requests.isEmpty());
    }

    @Test
    void theFactoryIsSesAndBuildsNoClientUntilTheFirstSend() {
        SesEmailSenderProviderFactory factory = new SesEmailSenderProviderFactory();
        factory.init(null);
        factory.postInit(null);

        assertEquals("ses", factory.getId());
        assertNotNull(factory.create(null));
        assertFalse(factory.built());
        factory.close();
    }

    @Test
    void keycloakFindsTheFactory() {
        List<String> ids = ServiceLoader.load(EmailSenderProviderFactory.class).stream()
                .map(p -> p.get().getId())
                .toList();

        assertEquals(List.of("ses"), ids);
    }
}
