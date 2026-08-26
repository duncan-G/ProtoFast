<#import "template.ftl" as layout>

<#--
  Overrides base/login/webauthn-register.ftl. The WebAuthn call itself is copied
  verbatim — only the markup around it differs, in three deliberate ways:

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

  3. It is labelled "Skip". Nothing is being cancelled — the user is signed in
     either way and is declining an offer.

  Both buttons live in .pf-buttons: Register is a bare <input> and Skip has to
  carry its own <form>, so they are siblings with no common form to space them.

  The Register click also parks the ceremony input in sessionStorage. The error
  page has no challenge of its own (Keycloak's createWebAuthnErrorPage does not
  pass one), and navigator.credentials.create() needs a user gesture, so "Try
  again" cannot round-trip through this page and still open the prompt. It
  reuses this stash instead. The challenge is still the one in AUTH_CHALLENGE_NOTE:
  a dismissed prompt does not rotate it.
-->
<@layout.registrationLayout; section>
    <#if section = "header">
        <span class="${properties.kcWebAuthnKeyIcon!}"></span>
        ${msg("webauthn-registration-title")}
    <#elseif section = "form">

        <form id="register" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">
            <input type="hidden" id="clientDataJSON" name="clientDataJSON"/>
            <input type="hidden" id="attestationObject" name="attestationObject"/>
            <input type="hidden" id="publicKeyCredentialId" name="publicKeyCredentialId"/>
            <input type="hidden" id="authenticatorLabel" name="authenticatorLabel"/>
            <input type="hidden" id="transports" name="transports"/>
            <input type="hidden" id="authenticatorAttachment" name="authenticatorAttachment"/>
            <input type="hidden" id="error" name="error"/>
        </form>

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
                    initLabelPrompt : ${msg("webauthn-registration-init-label-prompt")?c},
                    errmsg : ${msg("webauthn-unsupported-browser-text")?c}
                };
                sessionStorage.setItem('pf.webauthnRegister', JSON.stringify(input));
                registerByWebAuthn(input);
            }, { once: true });
            </#outputformat>
        </script>

        <div class="${properties.kcFormButtonsClass!}">
            <input type="submit"
                   class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                   id="registerWebAuthn" value="${msg("doRegisterSecurityKey")}"/>

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

    </#if>
</@layout.registrationLayout>
