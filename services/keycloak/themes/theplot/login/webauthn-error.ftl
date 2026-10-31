<#import "template.ftl" as layout>

<#--
  Overrides base/login/webauthn-error.ftl. This is the card a user lands on
  after dismissing the browser's passkey prompt, so it is the same offer as
  webauthn-register.ftl — and on the registration path it is now literally the
  same card: medallion, title, body copy, the privacy note, and a primary
  button that turns into the waiting state on click. Only the alert bar above
  it says anything went wrong, and "Try again" clears that bar as it opens the
  browser prompt, exactly as the Register button does one page earlier. A user
  who dismisses the prompt twice sees the same card each time rather than one
  layout for the offer and a different one for the retry.

  This template is shared with passkey *sign-in* failures. That path keeps the
  plain error card: it has no ceremony input of its own to reopen, so "Try
  again" reposts the flow with isSetRetry set, which is what base does.

  Registration "Try again" does what Register did: navigator.credentials.create()
  in the click handler. A dismissed prompt does not rotate AUTH_CHALLENGE_NOTE,
  and this page is not given a challenge, so the Register click on
  webauthn-register.ftl parks the ceremony input in sessionStorage for us to
  reuse. Posting isSetRetry instead would render the Register page again, and
  auto-starting the prompt there would fail — create() needs a user gesture,
  which a form-POST navigation is not. If the stash is missing (refresh of a
  cold tab), we fall back to that post so the user is not stuck.

  Because the card is the register card, the naming step is here too: a retry
  that succeeds lands on the same "Passkey saved" step it would have reached
  from the register page, instead of webauthnRegister.js's no-name-step
  fallback silently posting the suggestion. That is why #register carries no
  hidden authenticatorLabel on this path — the visible field is the one that
  owns that name.
-->
<@layout.registrationLayout displayMessage=true; section>

    <#assign pfRegisterRetry = ((webAuthnTitle)!'') == "webauthn-registration-title">

    <#if section = "header">
        <#if pfRegisterRetry>
            <span class="pf-medallion" aria-hidden="true">
                <span class="${properties.kcWebAuthnDefaultIcon!}"></span>
            </span>
            <span id="pf-passkey-title" aria-live="polite">${msg("webauthn-registration-title")}</span>
        <#else>
            ${kcSanitize(msg("webauthn-error-title"))?no_esc}
        </#if>
    <#elseif section = "form">

        <#if pfRegisterRetry>
        <form id="register" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">
            <input type="hidden" id="clientDataJSON" name="clientDataJSON"/>
            <input type="hidden" id="attestationObject" name="attestationObject"/>
            <input type="hidden" id="publicKeyCredentialId" name="publicKeyCredentialId"/>
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

        <#if pfRegisterRetry>
        <div class="pf-passkey">
            <p class="pf-passkey-body" id="pf-passkey-body" aria-live="polite">${msg("webauthn-registration-body")}</p>

            <#-- Same naming step as webauthn-register.ftl, revealed by
                 webauthnRegister.js once the credential exists, and hidden for
                 the same reason it carries no `required`: returnFailure() also
                 submits #register with this field still hidden, and a
                 display:none control that fails constraint validation blocks
                 the submit entirely. -->
            <div class="${properties.kcFormGroupClass!}" id="pf-passkey-name" hidden>
                <label class="${properties.kcLabelClass!}" for="authenticatorLabel">${msg("webauthn-registration-label-field")}</label>
                <input type="text" id="authenticatorLabel" name="authenticatorLabel" form="register"
                       class="${properties.kcInputClass!}" maxlength="255"
                       autocomplete="off" autocapitalize="words" spellcheck="false"/>
                <p class="${properties.kcInputHelperTextClass!}">${msg("webauthn-registration-label-help")}</p>
            </div>

            <div class="${properties.kcFormButtonsClass!}" id="pf-passkey-actions">
                <#-- A <button>, not base's <input type="button">: the waiting
                     state nests a spinner inside it, which an <input> cannot
                     hold. It submits nothing itself — the click handler either
                     reopens the prompt or posts one of the forms above. -->
                <button type="button" tabindex="4"
                        class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                        name="try-again" id="kc-try-again">
                    <span class="pf-spinner" aria-hidden="true"></span>
                    <span id="pf-passkey-cta">${kcSanitize(msg("doTryAgain"))?no_esc}</span>
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
        <#else>
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
        </#if>

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

            const tryAgain = document.getElementById('kc-try-again');

            tryAgain.addEventListener('click', () => {
                // The prompt is open on this same page; the banner still saying
                // setup was cancelled would be lying for the length of it.
                document.querySelector('.pf-alert')?.remove();
                <#if pfRegisterRetry>
                const raw = sessionStorage.getItem('pf.webauthnRegister');
                if (raw) {
                    let input;
                    try {
                        input = JSON.parse(raw);
                    } catch (e) {
                        // stash unreadable — fall through to the Register page
                    }
                    if (input) {
                        // Same handover as the Register button: the browser
                        // prompt is modal to the tab but this page stays visible
                        // behind it, so say what we are waiting for rather than
                        // keep offering something already asked for. Every
                        // outcome — success, dismissal, timeout — leaves this
                        // state, so it needs no way back.
                        document.getElementById('pf-passkey-title').textContent = ${msg("webauthn-registration-waiting-title")?c};
                        document.getElementById('pf-passkey-body').textContent = ${msg("webauthn-registration-waiting-body")?c};
                        document.getElementById('pf-passkey-cta').textContent = ${msg("webauthn-registration-waiting-cta")?c};
                        tryAgain.setAttribute('aria-busy', 'true');
                        tryAgain.disabled = true;

                        registerByWebAuthn(input);
                        return;
                    }
                }
                </#if>
                retryExecution();
            });
            </#outputformat>
        </script>

    </#if>
</@layout.registrationLayout>
