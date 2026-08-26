<#import "template.ftl" as layout>

<#--
  Overrides base/login/webauthn-error.ftl. This is the card a user lands on
  after dismissing the browser's passkey prompt, so it is the same offer as
  webauthn-register.ftl and is worded and spaced the same way: the escape hatch
  says "Skip", not "Cancel", and both buttons sit in .pf-buttons rather than
  relying on a margin between two unrelated siblings.

  This template is shared with passkey *sign-in* failures. Sign-in "Try again"
  still reposts the flow with isSetRetry set, which is what base does.

  Registration "Try again" does what Register did: navigator.credentials.create()
  in the click handler. A dismissed prompt does not rotate AUTH_CHALLENGE_NOTE,
  and this page is not given a challenge, so the Register click on
  webauthn-register.ftl parks the ceremony input in sessionStorage for us to
  reuse. Posting isSetRetry instead would render the Register page again, and
  auto-starting the prompt there would fail — create() needs a user gesture,
  which a form-POST navigation is not. If the stash is missing (refresh of a
  cold tab), we fall back to that post so the user is not stuck.
-->
<@layout.registrationLayout displayMessage=true; section>
    <#if section = "header">
        ${kcSanitize(msg("webauthn-error-title"))?no_esc}
    <#elseif section = "form">

        <#assign pfRegisterRetry = ((webAuthnTitle)!'') == "webauthn-registration-title">

        <#if pfRegisterRetry>
        <form id="register" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">
            <input type="hidden" id="clientDataJSON" name="clientDataJSON"/>
            <input type="hidden" id="attestationObject" name="attestationObject"/>
            <input type="hidden" id="publicKeyCredentialId" name="publicKeyCredentialId"/>
            <input type="hidden" id="authenticatorLabel" name="authenticatorLabel"/>
            <input type="hidden" id="transports" name="transports"/>
            <input type="hidden" id="authenticatorAttachment" name="authenticatorAttachment"/>
            <input type="hidden" id="error" name="error"/>
        </form>
        </#if>

        <form id="kc-error-credential-form" class="${properties.kcFormClass!}" action="${url.loginAction}"
              method="post">
            <input type="hidden" id="executionValue" name="authenticationExecution"/>
            <input type="hidden" id="isSetRetry" name="isSetRetry"/>
        </form>

        <div class="${properties.kcFormButtonsClass!}">
            <input tabindex="4" type="button"
                   class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                   name="try-again" id="kc-try-again" value="${kcSanitize(msg("doTryAgain"))?no_esc}"
            />

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

        <script type="module">
            <#outputformat "JavaScript">
            <#if pfRegisterRetry>
            import { registerByWebAuthn } from "${url.resourcesPath}/js/webauthnRegister.js";
            </#if>

            const retryExecution = () => {
                document.getElementById('isSetRetry').value = 'retry';
                document.getElementById('executionValue').value = ${execution?c};
                document.getElementById('kc-error-credential-form').requestSubmit();
            };

            document.getElementById('kc-try-again').addEventListener('click', () => {
                // The prompt is open on this same page; the banner still saying
                // setup was cancelled would be lying for the length of it.
                document.querySelector('.pf-alert')?.remove();
                <#if pfRegisterRetry>
                const raw = sessionStorage.getItem('pf.webauthnRegister');
                if (raw) {
                    try {
                        registerByWebAuthn(JSON.parse(raw));
                        return;
                    } catch (e) {
                        // stash unreadable — fall through to the Register page
                    }
                }
                </#if>
                retryExecution();
            });
            </#outputformat>
        </script>

    </#if>
</@layout.registrationLayout>
