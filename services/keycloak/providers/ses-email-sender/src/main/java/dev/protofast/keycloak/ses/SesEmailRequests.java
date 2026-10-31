package dev.protofast.keycloak.ses;

import java.net.IDN;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Map;
import java.util.regex.Pattern;

import org.keycloak.email.EmailException;
import org.keycloak.utils.EmailValidationUtil;

import software.amazon.awssdk.services.sesv2.model.Body;
import software.amazon.awssdk.services.sesv2.model.Content;
import software.amazon.awssdk.services.sesv2.model.Destination;
import software.amazon.awssdk.services.sesv2.model.EmailContent;
import software.amazon.awssdk.services.sesv2.model.Message;
import software.amazon.awssdk.services.sesv2.model.SendEmailRequest;

/**
 * SesEmailRequests turns what Keycloak hands an email sender into SES's SendEmail request:
 * the realm's {@code smtpServer} settings, one recipient, a subject and the bodies. It
 * calls nothing, so it is tested on its own.
 *
 * <p>Of the realm's settings it reads {@code from}, {@code fromDisplayName}, {@code replyTo}
 * and {@code replyToDisplayName}. SMTP's settings (host, port, auth, ssl, starttls,
 * envelopeFrom) mean nothing to SES and are ignored: SES signs with the domain's DKIM and
 * bounces to its MAIL FROM domain itself.
 *
 * <p>SES takes 7-bit ASCII before an address's {@code @}, a domain in Punycode, and a
 * display name in MIME encoded-words, so the realm's {@code allowutf8} can't change that.
 * Error messages never hold an address: Keycloak logs them.
 */
public final class SesEmailRequests {

    static final String FROM = "from";
    static final String FROM_DISPLAY_NAME = "fromDisplayName";
    static final String REPLY_TO = "replyTo";
    static final String REPLY_TO_DISPLAY_NAME = "replyToDisplayName";

    private static final String CHARSET = "UTF-8";

    /** RFC 2047: an encoded-word is at most 75 characters, "=?UTF-8?B?" and "?=" included. */
    private static final int MAX_ENCODED_WORD = 75;
    private static final int ENCODED_WORD_OVERHEAD = "=?UTF-8?B??=".length();
    /** The most UTF-8 bytes whose Base64 fits in one encoded-word: 45 bytes are 60 characters. */
    private static final int MAX_ENCODED_WORD_BYTES = (MAX_ENCODED_WORD - ENCODED_WORD_OVERHEAD) / 4 * 3;

    /**
     * Anything shaped like an address, with what surrounds it in SES's messages left out. The
     * domain needs a dot and a top-level domain, so the SDK's object names, such as
     * {@code SystemSettingsRegionProvider@5b1f0f26}, are left alone, and the local part
     * stops at a colon or slash, so an ARN keeps all but the address.
     */
    private static final Pattern ADDRESS = Pattern.compile(
            "[^\\s'\"<>,;:/()\\[\\]`@]+@(?:[A-Za-z0-9-]+\\.)+[A-Za-z][A-Za-z0-9-]*");

    private SesEmailRequests() {
    }

    /**
     * build makes the SendEmail request for one message.
     *
     * @throws EmailException when the From, Reply-To or recipient address isn't one SES
     *     takes, or there's no body. The message never holds the address.
     */
    public static SendEmailRequest build(Map<String, String> config, String to, String subject,
            String textBody, String htmlBody) throws EmailException {
        Map<String, String> settings = config == null ? Map.of() : config;
        String from = address(settings.get(FROM), "The realm's From address (from) is missing or isn't a valid email address");
        String recipient = address(to, "The recipient's email address isn't one Amazon SES can send to");
        if (textBody == null && htmlBody == null) {
            throw new EmailException("The message has no text or HTML body");
        }

        Body.Builder body = Body.builder();
        if (textBody != null) {
            body.text(content(textBody));
        }
        if (htmlBody != null) {
            body.html(content(htmlBody));
        }

        SendEmailRequest.Builder request = SendEmailRequest.builder()
                .fromEmailAddress(mailbox(from, settings.get(FROM_DISPLAY_NAME)))
                .destination(Destination.builder().toAddresses(recipient).build())
                .content(EmailContent.builder()
                        .simple(Message.builder()
                                .subject(content(subject == null ? "" : subject))
                                .body(body.build())
                                .build())
                        .build());

        String replyTo = settings.get(REPLY_TO);
        if (!isBlank(replyTo)) {
            String address = address(replyTo, "The realm's Reply-To address (replyTo) isn't a valid email address");
            request.replyToAddresses(mailbox(address, settings.get(REPLY_TO_DISPLAY_NAME)));
        }
        return request.build();
    }

    /**
     * validate checks the realm's settings as Keycloak saves them: a From address SES
     * takes, and a Reply-To address if there is one.
     */
    public static void validate(Map<String, String> config) throws EmailException {
        Map<String, String> settings = config == null ? Map.of() : config;
        address(settings.get(FROM), "The realm's From address (from) is missing or isn't a valid email address");
        String replyTo = settings.get(REPLY_TO);
        if (!isBlank(replyTo)) {
            address(replyTo, "The realm's Reply-To address (replyTo) isn't a valid email address");
        }
    }

    /**
     * address checks an address and returns it as SES takes it: trimmed, with its domain in
     * Punycode. It refuses one that isn't valid or has a non-ASCII local part.
     */
    static String address(String value, String problem) throws EmailException {
        if (isBlank(value)) {
            throw new EmailException(problem);
        }
        String trimmed = value.trim();
        if (!EmailValidationUtil.isValidEmail(trimmed, EmailValidationUtil.MAX_LOCAL_PART_LENGTH)) {
            throw new EmailException(problem);
        }
        int at = trimmed.lastIndexOf('@');
        String local = trimmed.substring(0, at);
        if (!local.chars().allMatch(c -> c < 128)) {
            throw new EmailException(problem + ": Amazon SES takes only ASCII before the @");
        }
        String domain;
        try {
            domain = IDN.toASCII(trimmed.substring(at + 1));
        } catch (IllegalArgumentException e) {
            throw new EmailException(problem);
        }
        return local + "@" + domain;
    }

    /**
     * mailbox is an address with its display name, if any: a quoted string for printable
     * ASCII, otherwise MIME encoded-words. Control characters, such as a line break, become
     * spaces, so a display name can't add a header.
     */
    static String mailbox(String address, String displayName) {
        if (isBlank(displayName)) {
            return address;
        }
        String name = displayName.replaceAll("\\p{Cntrl}", " ").trim();
        if (name.isEmpty()) {
            return address;
        }
        boolean printableAscii = name.chars().allMatch(c -> c >= 0x20 && c < 0x7f);
        String phrase = printableAscii
                ? "\"" + name.replace("\\", "\\\\").replace("\"", "\\\"") + "\""
                : String.join(" ", encodedWords(name));
        return phrase + " <" + address + ">";
    }

    /**
     * encodedWords splits text into RFC 2047 B encoded-words of at most 75 characters each,
     * never splitting a character's UTF-8 bytes between two words.
     */
    static List<String> encodedWords(String text) {
        List<String> words = new ArrayList<>();
        StringBuilder chunk = new StringBuilder();
        int bytes = 0;
        for (int i = 0; i < text.length(); ) {
            int codePoint = text.codePointAt(i);
            String character = new String(Character.toChars(codePoint));
            int size = character.getBytes(StandardCharsets.UTF_8).length;
            if (bytes + size > MAX_ENCODED_WORD_BYTES) {
                words.add(encodedWord(chunk.toString()));
                chunk.setLength(0);
                bytes = 0;
            }
            chunk.append(character);
            bytes += size;
            i += Character.charCount(codePoint);
        }
        if (!chunk.isEmpty()) {
            words.add(encodedWord(chunk.toString()));
        }
        return words;
    }

    private static String encodedWord(String text) {
        return "=?UTF-8?B?" + Base64.getEncoder().encodeToString(text.getBytes(StandardCharsets.UTF_8)) + "?=";
    }

    /**
     * redact replaces anything shaped like an email address with {@code <address>}. SES puts
     * recipients in some errors, such as an unverified address in the sandbox, and Keycloak
     * logs what a sender throws.
     */
    static String redact(String message) {
        return message == null ? "" : ADDRESS.matcher(message).replaceAll("<address>");
    }

    private static Content content(String data) {
        return Content.builder().data(data).charset(CHARSET).build();
    }

    private static boolean isBlank(String value) {
        return value == null || value.isBlank();
    }
}
