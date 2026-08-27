<#import "template.ftl" as layout>

<#--
  Overrides base/login/webauthn-register.ftl. The WebAuthn call itself is copied
  verbatim — only the markup around it differs, in five deliberate ways:

  1. No "Sign out from other devices" checkbox. Base renders
     <@passwordCommons.logoutOtherSessions/> here, the same control it puts on
     the credential-update pages. Adding a passkey invalidates nothing the user
     already holds, so there is no reason to offer to end their other sessions
     from this page — and the page could not make that offer conditional even if
     we wanted it to: nothing in the FreeMarker context carries a session count.
     Ending other sessions belongs with the account page, which can count them.

  2. The way out survives a retry. Base gates the button on
     `!isSetRetry?has_content`, so a user who dismisses the browser's passkey
     prompt, lands on webauthn-error.ftl and presses "Try again" comes back to
     this page with no way out but the browser's back button. Whether the action
     was app-initiated is the only thing the button actually depends on.

  3. It is labelled "Cancel", with no destination attached. It cannot be
     "Cancel and return to account": this same page is what a brand-new user
     meets on the way out of their first sign-in, before they have ever seen
     the account page, so a way *back* to it would be a promise the page cannot
     keep. The bare label is true in both entries: app-initiated from the
     account page, and offered after login.

  4. It explains the offer before asking for a fingerprint, and says so while
     the browser prompt is open. The medallion, the body copy, the two states
     of the primary button and the note about what leaves the device are the
     Nocturne passkey-setup design; the copy for both states lives in
     messages_en.properties.

  5. Naming the passkey happens on the card. Base calls window.prompt() the
     moment the credential exists, which the browser paints as a tab-modal
     dialog — one more grey box in a flow that has just shown the user two of
     them — prefilled with the literal "Passkey (Default Label)". The field
     below does the same job in the page's own voice, and the theme's copy of
     resources/js/webauthnRegister.js prefills it with a name derived from the
     credential: the passkey provider's own name where the AAGUID says who it
     is, the kind of device otherwise. That file carries the reasoning.

  Both buttons live in .pf-buttons: Register is a bare <button> and Cancel has
  to carry its own <form>, so they are siblings with no common form to space
  them. Save is not among them — it replaces the pair rather than joining it,
  so it sits outside #pf-passkey-actions, which is what gets hidden.

  The Register click also parks the ceremony input in sessionStorage. The error
  page has no challenge of its own (Keycloak's createWebAuthnErrorPage does not
  pass one), and navigator.credentials.create() needs a user gesture, so "Try
  again" cannot round-trip through this page and still open the prompt. It
  reuses this stash instead. The challenge is still the one in AUTH_CHALLENGE_NOTE:
  a dismissed prompt does not rotate it.
-->
<@layout.registrationLayout; section>
    <#if section = "header">
        <span class="pf-medallion" aria-hidden="true">
            <span class="${properties.kcWebAuthnDefaultIcon!}"></span>
        </span>
        <span id="pf-passkey-title" aria-live="polite">${msg("webauthn-registration-title")}</span>
    <#elseif section = "form">

        <form id="register" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">
            <input type="hidden" id="clientDataJSON" name="clientDataJSON"/>
            <input type="hidden" id="attestationObject" name="attestationObject"/>
            <input type="hidden" id="publicKeyCredentialId" name="publicKeyCredentialId"/>
            <input type="hidden" id="transports" name="transports"/>
            <input type="hidden" id="authenticatorAttachment" name="authenticatorAttachment"/>
            <input type="hidden" id="error" name="error"/>
        </form>

        <div class="pf-passkey">
            <p class="pf-passkey-body" id="pf-passkey-body" aria-live="polite">${msg("webauthn-registration-body")}</p>

            <#-- The naming step, revealed by webauthnRegister.js once the credential
                 exists. It is outside #register — that form holds only the ceremony's
                 hidden fields and sits above the visible card — so the field and its
                 button name #register as their form owner instead. Submitting posts
                 the label alongside the attestation, with no scripted submit.

                 No `required`: returnFailure() also submits #register, on a path
                 where this field is still hidden, and a display:none control that
                 fails constraint validation blocks the submit entirely. The script
                 restores the suggestion if the field is left empty. -->
            <div class="${properties.kcFormGroupClass!}" id="pf-passkey-name" hidden>
                <label class="${properties.kcLabelClass!}" for="authenticatorLabel">${msg("webauthn-registration-label-field")}</label>
                <input type="text" id="authenticatorLabel" name="authenticatorLabel" form="register"
                       class="${properties.kcInputClass!}" maxlength="255"
                       autocomplete="off" autocapitalize="words" spellcheck="false"/>
                <p class="${properties.kcInputHelperTextClass!}">${msg("webauthn-registration-label-help")}</p>
            </div>

            <div class="${properties.kcFormButtonsClass!}" id="pf-passkey-actions">
                <#-- Not type="submit": this button sits outside #register and never
                     submits anything itself — registerByWebAuthn fills the hidden
                     inputs, and from there either the naming step's Save posts the
                     form or returnFailure() does. A real <button> also lets the
                     waiting state nest its own spinner, which an <input> cannot. -->
                <button type="button"
                        class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                        id="registerWebAuthn">
                    <span class="pf-spinner" aria-hidden="true"></span>
                    <span id="pf-passkey-cta">${msg("doRegisterSecurityKey")}</span>
                </button>

                <#if isAppInitiatedAction??>
                    <form action="${url.loginAction}" class="${properties.kcFormClass!}" id="kc-webauthn-settings-form"
                          method="post">
                        <button type="submit"
                                class="${properties.kcButtonClass!} ${properties.kcButtonDefaultClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                                id="cancelWebAuthnAIA" name="cancel-aia" value="true">${msg("doSkipPasskey")}
                        </button>
                    </form>
                </#if>
            </div>

            <button type="submit" form="register" id="pf-passkey-save" hidden
                    class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}">
                <span class="pf-spinner" aria-hidden="true"></span>
                ${msg("webauthn-registration-label-save")}
            </button>

            <p class="pf-passkey-privacy">
                <span class="pf-glyph-lock" aria-hidden="true"></span>
                ${msg("webauthn-registration-privacy")}
            </p>
        </div>

        <script type="module">
            <#outputformat "JavaScript">
            import { registerByWebAuthn } from "${url.resourcesPath}/js/webauthnRegister.js";
            const registerButton = document.getElementById('registerWebAuthn');
            registerButton.addEventListener("click", function() {
                const input = {
                    challenge : ${challenge?c},
                    userid : ${userid?c},
                    username : ${username?c},
                    signatureAlgorithms : [<#list signatureAlgorithms as sigAlg>${sigAlg?c},</#list>],
                    rpEntityName : ${rpEntityName?c},
                    rpId : ${rpId?c},
                    attestationConveyancePreference : ${attestationConveyancePreference?c},
                    authenticatorAttachment : ${authenticatorAttachment?c},
                    requireResidentKey : ${requireResidentKey?c},
                    residentKey : ${residentKey?c},
                    userVerificationRequirement : ${userVerificationRequirement?c},
                    createTimeout : ${createTimeout?c},
                    excludeCredentialIds : ${excludeCredentialIds?c},
                    initLabel : ${msg("webauthn-registration-init-label")?c},
                    namedTitle : ${msg("webauthn-registration-named-title")?c},
                    namedBody : ${msg("webauthn-registration-named-body")?c},
                    <#-- Names for the fallback the script reaches when the credential
                         reports no AAGUID we recognise. They describe a kind of device,
                         never a specific one: nothing in WebAuthn carries the name a
                         user gave their own machine. -->
                    deviceNames : {
                        windows : ${msg("webauthn-registration-device-windows")?c},
                        mac : ${msg("webauthn-registration-device-mac")?c},
                        iphone : ${msg("webauthn-registration-device-iphone")?c},
                        ipad : ${msg("webauthn-registration-device-ipad")?c},
                        android : ${msg("webauthn-registration-device-android")?c},
                        chromeos : ${msg("webauthn-registration-device-chromeos")?c},
                        linux : ${msg("webauthn-registration-device-linux")?c},
                        thisDevice : ${msg("webauthn-registration-device-this")?c},
                        phone : ${msg("webauthn-registration-device-phone")?c},
                        securityKey : ${msg("webauthn-registration-device-securitykey")?c}
                    },
                    errmsg : ${msg("webauthn-unsupported-browser-text")?c}
                };
                sessionStorage.setItem('pf.webauthnRegister', JSON.stringify(input));

                // The browser prompt is modal to the tab but the page stays visible
                // behind it, and a dismissed prompt leaves this page on screen until
                // registerByWebAuthn posts the error. Say what we are waiting for
                // rather than keep offering something already asked for. There is no
                // way back from this state on purpose: every WebAuthn outcome —
                // success, dismissal, timeout — navigates away.
                document.getElementById('pf-passkey-title').textContent = ${msg("webauthn-registration-waiting-title")?c};
                document.getElementById('pf-passkey-body').textContent = ${msg("webauthn-registration-waiting-body")?c};
                document.getElementById('pf-passkey-cta').textContent = ${msg("webauthn-registration-waiting-cta")?c};
                registerButton.setAttribute('aria-busy', 'true');
                registerButton.disabled = true;

                registerByWebAuthn(input);
            }, { once: true });
            </#outputformat>
        </script>

    </#if>
</@layout.registrationLayout>
