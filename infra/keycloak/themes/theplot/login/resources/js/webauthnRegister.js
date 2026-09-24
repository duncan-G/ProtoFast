/* Overrides base/login/resources/js/webauthnRegister.js.

   Everything up to the point where navigator.credentials.create() resolves is
   base's, verbatim. The ceremony is the part that must not drift from the
   server's expectations, and there is nothing in it this theme wants to change.

   What changes is what happens next. Base asks for the credential's label with

       window.prompt(initLabelPrompt, initLabel)

   which the browser paints as a tab-modal dialog — Firefox's is a grey bar that
   drops from the toolbar, unstyleable and easily read as one more browser
   passkey prompt rather than as this application asking a question. It is also
   prefilled with the literal string "Passkey (Default Label)", so whoever hits
   Enter has that stored as the name of their credential forever.

   This copy fills the same hidden field from an ordinary field on the card
   (revealed by showNameStep) and prefills it with a name derived from the
   credential itself. Both changes are confined to returnSuccess.

   Nothing here submits on success. Base does, right after the prompt closes;
   here the naming field's own submit button is what posts #register. A user who
   abandons the page at that point has a passkey on their device that Keycloak
   never recorded — the same hole base's prompt has, and the same recovery:
   the excludeCredentials list will not contain it, so registering again works. */

import { base64url } from "rfc4648";
import { AAGUID_NAMES } from "./aaguidNames.js";

export async function registerByWebAuthn(input) {

    // Check if WebAuthn is supported by this browser
    if (!window.PublicKeyCredential) {
        returnFailure(input.errmsg);
        return;
    }

    const publicKey = {
        challenge: base64url.parse(input.challenge, {loose: true}),
        rp: {id: input.rpId, name: input.rpEntityName},
        user: {
            id: base64url.parse(input.userid, {loose: true}),
            name: input.username,
            displayName: input.username
        },
        pubKeyCredParams: getPubKeyCredParams(input.signatureAlgorithms),
    };

    if (input.attestationConveyancePreference !== 'not specified') {
        publicKey.attestation = input.attestationConveyancePreference;
    }

    const authenticatorSelection = {};
    let isAuthenticatorSelectionSpecified = false;

    if (input.authenticatorAttachment !== 'not specified') {
        authenticatorSelection.authenticatorAttachment = input.authenticatorAttachment;
        isAuthenticatorSelectionSpecified = true;
    }

    if (input.residentKey && input.residentKey !== 'not specified') {
        // residentKey is the current spec field and the source of truth. requireResidentKey is
        // deprecated but still set for older clients: it is true iff residentKey is 'required'.
        authenticatorSelection.residentKey = input.residentKey;
        authenticatorSelection.requireResidentKey = input.residentKey === 'required';
        isAuthenticatorSelectionSpecified = true;
    } else if (input.requireResidentKey !== 'not specified') {
        // fall back to the deprecated option when residentKey is not specified
        if (input.requireResidentKey === 'Yes') {
            authenticatorSelection.residentKey = 'required';
            authenticatorSelection.requireResidentKey = true;
        } else {
            authenticatorSelection.residentKey = 'discouraged';
            authenticatorSelection.requireResidentKey = false;
        }
        isAuthenticatorSelectionSpecified = true;
    }

    if (input.userVerificationRequirement !== 'not specified') {
        authenticatorSelection.userVerification = input.userVerificationRequirement;
        isAuthenticatorSelectionSpecified = true;
    }

    if (isAuthenticatorSelectionSpecified) {
        publicKey.authenticatorSelection = authenticatorSelection;
    }

    if (input.createTimeout !== 0) {
        publicKey.timeout = input.createTimeout * 1000;
    }

    const excludeCredentials = getExcludeCredentials(input.excludeCredentialIds);
    if (excludeCredentials.length > 0) {
        publicKey.excludeCredentials = excludeCredentials;
    }

    try {
        const result = await doRegister(publicKey);
        returnSuccess(result, input);
    } catch (error) {
        returnFailure(error);
    }
}

function doRegister(publicKey) {
    return navigator.credentials.create({publicKey});
}

function getPubKeyCredParams(signatureAlgorithmsList) {
    const pubKeyCredParams = [];
    if (signatureAlgorithmsList.length === 0) {
        pubKeyCredParams.push({type: "public-key", alg: -7});
        return pubKeyCredParams;
    }

    for (const entry of signatureAlgorithmsList) {
        pubKeyCredParams.push({
            type: "public-key",
            alg: entry
        });
    }

    return pubKeyCredParams;
}

function getExcludeCredentials(excludeCredentialIds) {
    const excludeCredentials = [];
    if (excludeCredentialIds === "") {
        return excludeCredentials;
    }

    for (const entry of excludeCredentialIds.split(',')) {
        excludeCredentials.push({
            type: "public-key",
            id: base64url.parse(entry, {loose: true})
        });
    }

    return excludeCredentials;
}

function getTransportsAsString(transportsList) {
    if (!Array.isArray(transportsList)) {
        return "";
    }

    return transportsList.join();
}

/* ── Naming the credential ───────────────────────────────────────────────── */

/* WebAuthn deliberately exposes no device name. There is no field anywhere in
   the credential that says "Duncan's ThinkPad": a name the user chose for their
   own machine is exactly the kind of stable, high-entropy string that would
   make a browser trivially fingerprintable, so the spec never carries one.

   Three things are knowable, in descending order of how much they actually say,
   and suggestLabel tries them in that order.

   1. The AAGUID — the authenticator's *model*, not the device. Sixteen bytes at
      offset 37 of authData, which getAuthenticatorData() hands over directly;
      the alternative is CBOR-decoding attestationObject, and shipping a CBOR
      parser to read one fixed-offset slice is not worth it. Where it resolves,
      it is a real answer from the authenticator: "Apple Passwords",
      "Google Password Manager", "1Password". See aaguidNames.js for why only
      passkey providers are in the table, and why the realm's `attestation:
      none` is what decides that.

   2. Attachment and transports — `platform` + `internal` means the credential
      lives on the machine in front of the user; `hybrid` means it went to a
      phone over the QR/Bluetooth handshake; `usb`/`nfc`/`ble` means something
      plugged in or tapped. This is coarse but never wrong.

   3. For case (1)-fails-and-it-is-a-platform-credential, the operating system
      the *browser* is running on, which for a platform authenticator is the
      same box. This is the only guess in the chain, and it describes the host,
      not the authenticator. Client hints where they exist, the user-agent
      string where they do not.

   Every string this returns other than an AAGUID name comes from
   `input.deviceNames`, which webauthn-register.ftl fills from
   messages_en.properties. */

const ZERO_AAGUID = "00000000-0000-0000-0000-000000000000";

function readAaguid(response) {
    // Firefox 119+, Chrome 85+, Safari 16.4+. Older browsers fall through to
    // the transport heuristic rather than gaining a CBOR decoder.
    if (typeof response.getAuthenticatorData !== "function") {
        return null;
    }

    let authData;
    try {
        authData = new Uint8Array(response.getAuthenticatorData());
    } catch (e) {
        return null;
    }

    // 32 bytes rpIdHash + 1 flags + 4 signCount, then the attested credential
    // data, which opens with the AAGUID. Absent entirely if the AT flag is
    // clear, which for a create() response should not happen — but a truncated
    // buffer must not become a label made of undefineds.
    if (authData.length < 53) {
        return null;
    }

    const hex = Array.from(authData.slice(37, 53), b => b.toString(16).padStart(2, "0")).join("");
    const aaguid = [
        hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)
    ].join("-");

    // Browsers zero the AAGUID rather than omit it when they decline to
    // identify the authenticator, which is the common case under
    // `attestation: none`.
    return aaguid === ZERO_AAGUID ? null : aaguid;
}

function osName(deviceNames) {
    // userAgentData.platform is Chromium-only and returns one of a short frozen
    // list. Firefox and Safari have neither it nor any replacement, so the
    // user-agent string is the fallback rather than the exception.
    const hinted = navigator.userAgentData?.platform;
    if (hinted) {
        const byHint = {
            "Windows": deviceNames.windows,
            "macOS": deviceNames.mac,
            "Android": deviceNames.android,
            "Chrome OS": deviceNames.chromeos,
            "Chromium OS": deviceNames.chromeos,
            "Linux": deviceNames.linux
        }[hinted];
        // Android reports "Android" for both phones and tablets, and iOS has no
        // client hints at all, so the two Apple handhelds are never reached
        // here — they come out of the user-agent branch below.
        if (byHint) {
            return byHint;
        }
    }

    const ua = navigator.userAgent;
    if (/iPhone/.test(ua)) return deviceNames.iphone;
    // iPadOS 13+ ships a desktop Safari user-agent that says Macintosh. Touch
    // points are what still separate them: a Mac reports 0, an iPad 5.
    if (/iPad/.test(ua)) return deviceNames.ipad;
    if (/Macintosh/.test(ua)) return navigator.maxTouchPoints > 1 ? deviceNames.ipad : deviceNames.mac;
    if (/Android/.test(ua)) return deviceNames.android;
    if (/CrOS/.test(ua)) return deviceNames.chromeos;
    if (/Windows/.test(ua)) return deviceNames.windows;
    if (/Linux/.test(ua)) return deviceNames.linux;
    return null;
}

function suggestLabel(result, input) {
    const names = input.deviceNames;

    const aaguid = readAaguid(result.response);
    if (aaguid && AAGUID_NAMES[aaguid]) {
        return AAGUID_NAMES[aaguid];
    }

    const transports = typeof result.response.getTransports === "function"
        ? (result.response.getTransports() || [])
        : [];

    // Order matters: an authenticator can advertise several transports, and the
    // one that describes where the credential actually ended up is the most
    // specific, not the first in the list.
    if (transports.includes("internal") || result.authenticatorAttachment === "platform") {
        return osName(names) || names.thisDevice;
    }
    if (transports.includes("hybrid")) {
        return names.phone;
    }
    if (transports.some(t => t === "usb" || t === "nfc" || t === "ble")) {
        return names.securityKey;
    }

    return input.initLabel;
}

/* The naming step. The markup is already on the card, hidden — it is copy, so
   it belongs in the template where the rest of the copy is, not in a string
   built here. This only swaps which parts are visible and seeds the field.

   The field's form owner is #register (via its `form` attribute) even though it
   sits outside it in the DOM, so its submit button posts the ceremony and the
   label together with no JavaScript involved in the submit itself. That is also
   why the field carries no `required`: #register is submitted programmatically
   by returnFailure too, on a path where the field is still hidden, and
   constraint validation on a display:none control fails a submit outright. */
function showNameStep(suggestion, input) {
    const step = document.getElementById("pf-passkey-name");
    const field = document.getElementById("authenticatorLabel");
    const save = document.getElementById("pf-passkey-save");
    const register = document.getElementById("register");

    if (!step || !field || !save) {
        // No naming step on the page — post what we have rather than strand the
        // ceremony. Keycloak will store the credential under initLabel.
        field && (field.value = suggestion);
        register.requestSubmit();
        return;
    }

    document.getElementById("pf-passkey-title").textContent = input.namedTitle;
    document.getElementById("pf-passkey-body").textContent = input.namedBody;
    document.getElementById("pf-passkey-actions").hidden = true;

    field.value = suggestion;
    step.hidden = false;
    save.hidden = false;
    field.focus();
    // Selected, not just focused: the suggestion is a good default but it is a
    // guess, and someone who wants to replace it should be able to type over it.
    field.select();

    register.addEventListener("submit", () => {
        // A user who clears the field gets the suggestion back rather than a
        // credential with a blank name. Trimmed because Keycloak stores the
        // label verbatim and a trailing space is invisible in every list that
        // later shows it.
        field.value = field.value.trim() || suggestion;
        save.disabled = true;
        save.setAttribute("aria-busy", "true");
    }, { once: true });
}

function returnSuccess(result, input) {
    document.getElementById("clientDataJSON").value = base64url.stringify(new Uint8Array(result.response.clientDataJSON), {pad: false});
    document.getElementById("attestationObject").value = base64url.stringify(new Uint8Array(result.response.attestationObject), {pad: false});
    document.getElementById("publicKeyCredentialId").value = base64url.stringify(new Uint8Array(result.rawId), {pad: false});

    if (typeof result.response.getTransports === "function") {
        const transports = result.response.getTransports();
        if (transports) {
            document.getElementById("transports").value = getTransportsAsString(transports);
        }
    } else {
        console.log("Your browser is not able to recognize supported transport media for the authenticator.");
    }

    if (result.authenticatorAttachment) {
        document.getElementById("authenticatorAttachment").value = result.authenticatorAttachment;
    }

    showNameStep(suggestLabel(result, input), input);
}

function returnFailure(err) {
    document.getElementById("error").value = err;
    document.getElementById("register").requestSubmit();
}
