package dev.protofast.keycloak.ses;

import java.util.Map;
import java.util.function.Supplier;

import org.jboss.logging.Logger;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.models.UserModel;

import software.amazon.awssdk.awscore.exception.AwsErrorDetails;
import software.amazon.awssdk.awscore.exception.AwsServiceException;
import software.amazon.awssdk.core.exception.SdkException;
import software.amazon.awssdk.services.sesv2.SesV2Client;
import software.amazon.awssdk.services.sesv2.model.SendEmailRequest;
import software.amazon.awssdk.services.sesv2.model.SendEmailResponse;

/**
 * SesEmailSenderProvider sends Keycloak's mail (verification, password resets, one-time
 * codes, the admin console's test message) with SES's v2 SendEmail, as the component's own
 * role. It logs that a message was sent and its message id, never a recipient or a body.
 */
final class SesEmailSenderProvider implements EmailSenderProvider {

    private static final Logger LOG = Logger.getLogger(SesEmailSenderProvider.class);

    private final Supplier<SesV2Client> ses;

    /** @param ses gives the factory's client, built on the first send. */
    SesEmailSenderProvider(Supplier<SesV2Client> ses) {
        this.ses = ses;
    }

    @Override
    public void send(Map<String, String> config, UserModel user, String subject, String textBody,
            String htmlBody) throws EmailException {
        String address = user == null ? null : user.getEmail();
        if (address == null || address.isBlank()) {
            throw new EmailException("No email address configured for the user");
        }
        send(config, address, subject, textBody, htmlBody);
    }

    @Override
    public void send(Map<String, String> config, String address, String subject, String textBody,
            String htmlBody) throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(config, address, subject, textBody, htmlBody);
        SendEmailResponse response;
        try {
            response = ses.get().sendEmail(request);
        } catch (SdkException e) {
            // The SDK's exception isn't kept as the cause: Keycloak logs the cause too, and
            // SES's message can name the recipient.
            String problem = describe(e);
            LOG.warnf("Amazon SES didn't send the message: %s", problem);
            throw new EmailException("Amazon SES didn't send the message: " + problem);
        }
        LOG.infof("Sent an email with Amazon SES, message id %s", response.messageId());
    }

    @Override
    public void validate(Map<String, String> config) throws EmailException {
        SesEmailRequests.validate(config);
    }

    @Override
    public void close() {
    }

    /**
     * describe is what went wrong, with any address replaced: SES's error code, HTTP status,
     * request id and message, or the SDK's own message when SES wasn't reached (no region,
     * no credentials, no network).
     */
    static String describe(SdkException e) {
        if (e instanceof AwsServiceException service) {
            AwsErrorDetails details = service.awsErrorDetails();
            String code = details == null || details.errorCode() == null ? "unknown error" : details.errorCode();
            String message = details == null ? null : details.errorMessage();
            return code + " (HTTP " + service.statusCode() + ", request id " + service.requestId() + ")"
                    + (message == null || message.isBlank() ? "" : ": " + SesEmailRequests.redact(message));
        }
        return e.getClass().getSimpleName() + ": " + SesEmailRequests.redact(e.getMessage());
    }
}
