package dev.protofast.keycloak.ses;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import org.junit.jupiter.api.Test;
import org.keycloak.email.EmailException;

import software.amazon.awssdk.services.sesv2.model.Message;
import software.amazon.awssdk.services.sesv2.model.SendEmailRequest;

/**
 * Building SES's SendEmail request from a realm's smtpServer settings and a message. The
 * addresses are example.com's: no real address belongs in a fixture.
 */
class SesEmailRequestsTest {

    private static final String FROM = "no-reply@protofast.example.com";
    private static final String TO = "someone@example.com";

    private static Map<String, String> realm(String... pairs) {
        Map<String, String> config = new HashMap<>();
        // SMTP's settings, which a realm keeps alongside and SES ignores.
        config.put("host", "smtp4dev");
        config.put("port", "25");
        config.put("from", FROM);
        for (int i = 0; i < pairs.length; i += 2) {
            config.put(pairs[i], pairs[i + 1]);
        }
        return config;
    }

    @Test
    void aMessageHasTheRealmsFromAddressTheRecipientTheSubjectAndBothBodies() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(
                realm("fromDisplayName", "ProtoFast"), TO, "Verify email", "Click the link", "<p>Click the link</p>");

        assertEquals("\"ProtoFast\" <no-reply@protofast.example.com>", request.fromEmailAddress());
        assertEquals(List.of(TO), request.destination().toAddresses());
        assertFalse(request.destination().hasCcAddresses());
        assertFalse(request.destination().hasBccAddresses());
        assertFalse(request.hasReplyToAddresses());
        Message message = request.content().simple();
        assertEquals("Verify email", message.subject().data());
        assertEquals("UTF-8", message.subject().charset());
        assertEquals("Click the link", message.body().text().data());
        assertEquals("UTF-8", message.body().text().charset());
        assertEquals("<p>Click the link</p>", message.body().html().data());
        assertEquals("UTF-8", message.body().html().charset());
        assertNull(request.content().raw());
        assertNull(request.content().template());
    }

    @Test
    void withoutADisplayNameTheFromAddressIsBare() throws EmailException {
        assertEquals(FROM, SesEmailRequests.build(realm(), TO, "s", "t", "h").fromEmailAddress());
        assertEquals(FROM, SesEmailRequests.build(realm("fromDisplayName", "  "), TO, "s", "t", "h").fromEmailAddress());
    }

    @Test
    void addressesAreTrimmed() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(realm("from", "  " + FROM + " "), " " + TO + " ", "s", "t", null);

        assertEquals(FROM, request.fromEmailAddress());
        assertEquals(List.of(TO), request.destination().toAddresses());
    }

    @Test
    void aDisplayNameWithQuotesOrBackslashesIsEscaped() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(
                realm("fromDisplayName", "Proto \"App\" \\ Team"), TO, "s", "t", "h");

        assertEquals("\"Proto \\\"App\\\" \\\\ Team\" <no-reply@protofast.example.com>", request.fromEmailAddress());
    }

    @Test
    void aDisplayNameCantAddAHeader() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(
                realm("fromDisplayName", "ProtoFast\r\nBcc: someone@example.org"), TO, "s", "t", "h");

        assertFalse(request.fromEmailAddress().contains("\r"));
        assertFalse(request.fromEmailAddress().contains("\n"));
        assertEquals("\"ProtoFast  Bcc: someone@example.org\" <no-reply@protofast.example.com>", request.fromEmailAddress());
    }

    @Test
    void aNonAsciiDisplayNameIsAnEncodedWord() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(realm("fromDisplayName", "Équipe ProtoFast"), TO, "s", "t", "h");

        String from = request.fromEmailAddress();
        assertTrue(from.startsWith("=?UTF-8?B?"), from);
        assertTrue(from.endsWith("?= <no-reply@protofast.example.com>"), from);
        assertEquals("Équipe ProtoFast", decode(from.substring(0, from.indexOf(" <"))));
    }

    @Test
    void aLongNonAsciiDisplayNameIsSeveralEncodedWordsThatSplitNoCharacter() {
        // 30 four-byte characters, then two-byte ones: 160 bytes of UTF-8.
        String name = "😀".repeat(30) + "é".repeat(20);

        List<String> words = SesEmailRequests.encodedWords(name);

        assertTrue(words.size() > 1, words.toString());
        for (String word : words) {
            assertTrue(word.length() <= 75, word + " is " + word.length() + " characters");
            assertTrue(word.startsWith("=?UTF-8?B?") && word.endsWith("?="), word);
            // Each word decodes on its own, so none ends in half a character.
            String decoded = decode(word);
            assertFalse(decoded.contains("�"), word);
        }
        assertEquals(name, decode(String.join(" ", words)));
    }

    @Test
    void anInternationalDomainIsSentInPunycode() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(
                realm("from", "noreply@bücher.example"), "someone@bücher.example", "s", "t", "h");

        assertEquals("noreply@xn--bcher-kva.example", request.fromEmailAddress());
        assertEquals(List.of("someone@xn--bcher-kva.example"), request.destination().toAddresses());
    }

    @Test
    void aNonAsciiLocalPartIsRefusedEvenWithAllowUtf8() {
        EmailException e = assertThrows(EmailException.class, () -> SesEmailRequests.build(
                realm("allowutf8", "true"), "jürgen@example.com", "s", "t", "h"));

        assertTrue(e.getMessage().contains("only ASCII before the @"), e.getMessage());
        assertFalse(e.getMessage().contains("jürgen"), e.getMessage());
    }

    @Test
    void theReplyToAddressAndItsDisplayNameAreUsed() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(
                realm("replyTo", "support@protofast.example.com", "replyToDisplayName", "Support"), TO, "s", "t", "h");

        assertEquals(List.of("\"Support\" <support@protofast.example.com>"), request.replyToAddresses());
    }

    @Test
    void aBlankReplyToIsLeftOut() throws EmailException {
        SendEmailRequest request = SesEmailRequests.build(
                realm("replyTo", " ", "replyToDisplayName", "Support"), TO, "s", "t", "h");

        assertFalse(request.hasReplyToAddresses());
    }

    @Test
    void aMessageMayHaveOnlyAText() throws EmailException {
        Message message = SesEmailRequests.build(realm(), TO, "s", "text only", null).content().simple();

        assertEquals("text only", message.body().text().data());
        assertNull(message.body().html());
    }

    @Test
    void aMessageMayHaveOnlyHtml() throws EmailException {
        Message message = SesEmailRequests.build(realm(), TO, "s", null, "<p>html only</p>").content().simple();

        assertNull(message.body().text());
        assertEquals("<p>html only</p>", message.body().html().data());
    }

    @Test
    void aMessageWithNoBodyIsRefused() {
        assertThrows(EmailException.class, () -> SesEmailRequests.build(realm(), TO, "s", null, null));
    }

    @Test
    void aMissingSubjectIsEmpty() throws EmailException {
        assertEquals("", SesEmailRequests.build(realm(), TO, null, "t", null).content().simple().subject().data());
    }

    @Test
    void aSubjectAndBodiesKeepTheirUnicode() throws EmailException {
        Message message = SesEmailRequests.build(realm(), TO, "Vérifiez votre adresse ✉", "Merci 🙏", "<p>Merci 🙏</p>")
                .content().simple();

        assertEquals("Vérifiez votre adresse ✉", message.subject().data());
        assertEquals("Merci 🙏", message.body().text().data());
        assertEquals("<p>Merci 🙏</p>", message.body().html().data());
    }

    @Test
    void withoutAFromAddressNothingIsBuilt() {
        Map<String, String> config = realm();
        config.remove("from");

        EmailException e = assertThrows(EmailException.class, () -> SesEmailRequests.build(config, TO, "s", "t", "h"));
        assertTrue(e.getMessage().contains("From address"), e.getMessage());
        assertThrows(EmailException.class, () -> SesEmailRequests.build(null, TO, "s", "t", "h"));
    }

    @Test
    void anInvalidFromAddressIsRefusedWithoutRepeatingIt() {
        EmailException e = assertThrows(EmailException.class,
                () -> SesEmailRequests.build(realm("from", "not-an-address"), TO, "s", "t", "h"));

        assertFalse(e.getMessage().contains("not-an-address"), e.getMessage());
    }

    @Test
    void anInvalidRecipientIsRefusedWithoutRepeatingIt() {
        EmailException e = assertThrows(EmailException.class,
                () -> SesEmailRequests.build(realm(), "someone@@example.com", "s", "t", "h"));

        assertFalse(e.getMessage().contains("someone"), e.getMessage());
        assertThrows(EmailException.class, () -> SesEmailRequests.build(realm(), null, "s", "t", "h"));
        assertThrows(EmailException.class, () -> SesEmailRequests.build(realm(), "", "s", "t", "h"));
    }

    @Test
    void validateAcceptsAFromAddressAndAnOptionalReplyTo() {
        assertDoesNotThrow(() -> SesEmailRequests.validate(realm()));
        assertDoesNotThrow(() -> SesEmailRequests.validate(realm("replyTo", "support@protofast.example.com")));
        assertDoesNotThrow(() -> SesEmailRequests.validate(realm("replyTo", "")));
    }

    @Test
    void validateRefusesAMissingFromOrABadReplyTo() {
        Map<String, String> noFrom = realm();
        noFrom.remove("from");

        assertThrows(EmailException.class, () -> SesEmailRequests.validate(noFrom));
        assertThrows(EmailException.class, () -> SesEmailRequests.validate(realm("from", "nope")));
        assertThrows(EmailException.class, () -> SesEmailRequests.validate(realm("replyTo", "nope")));
    }

    @Test
    void redactReplacesEveryAddress() {
        String redacted = SesEmailRequests.redact(
                "Email address is not verified. The following identities failed the check in region US-EAST-1: "
                        + "someone@example.com, other.person+tag@sub.example.org; "
                        + "not authorized on resource `arn:aws:ses:us-east-1:123456789012:identity/someone@example.com'");

        assertFalse(redacted.contains("someone"), redacted);
        assertFalse(redacted.contains("other.person"), redacted);
        assertTrue(redacted.contains("failed the check in region US-EAST-1: <address>, <address>;"), redacted);
        assertTrue(redacted.endsWith("identity/<address>'"), redacted);
        assertEquals("", SesEmailRequests.redact(null));
        assertEquals("Throttling: Maximum sending rate exceeded.",
                SesEmailRequests.redact("Throttling: Maximum sending rate exceeded."));
    }

    @Test
    void redactLeavesTheSdksObjectNamesAlone() {
        // The SDK's chains name each provider they tried by its toString().
        String message = "Unable to load region from any of the providers in the chain "
                + "software.amazon.awssdk.regions.providers.DefaultAwsRegionProviderChain@6d2a209c: "
                + "[software.amazon.awssdk.regions.providers.SystemSettingsRegionProvider@75d0c1d4: Unable to load region.]";

        assertEquals(message, SesEmailRequests.redact(message));
    }

    /** decode reads RFC 2047 B encoded-words separated by spaces. */
    private static String decode(String words) {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        for (String word : words.split(" ")) {
            assertTrue(word.startsWith("=?UTF-8?B?") && word.endsWith("?="), word);
            bytes.writeBytes(Base64.getDecoder().decode(word.substring(10, word.length() - 2)));
        }
        return bytes.toString(StandardCharsets.UTF_8);
    }
}
