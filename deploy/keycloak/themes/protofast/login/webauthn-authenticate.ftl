<#import "template.ftl" as layout>

<#--
  Overrides base/login/webauthn-authenticate.ftl — the passkey step of the
  `passwordless credentials` flow, reached once auth-username-form has named the
  user. Two things differ from base.

  1. The ceremony starts on its own. By the time this page renders, the user has
     already said who they are and pressed Continue; base then shows a card whose
     only content is a "Use passkey" button, so the ceremony costs a second click
     that asks nothing new. We open navigator.credentials.get() on load instead,
     and the card renders as the waiting state behind the browser's prompt.

     The button stays in the markup, for two cases that both need a real click:
     a browser that refuses a WebAuthn call without user activation (Safari), and
     a browser with no module support at all, where nothing below runs and the
     button is the whole page — base's behaviour, unregressed.

  2. No list of registered authenticators. Base renders one when
     `shouldDisplayAuthenticators` is set, to pick a credential before the prompt
     opens. The prompt itself is the picker once it opens by itself, and a list
     the user never asked for would be the click we just removed. The hidden
     `authn_select` form stays — that is what scopes the ceremony to this user's
     credentials, and getAllowCredentials() reads it.

  The waiting copy is the passkey-setup page's, one step over: same medallion,
  same body-then-buttons stack, same spinner-in-the-button busy state.
-->
<@layout.registrationLayout displayInfo=false; section>
    <#if section = "header">
        <span class="pf-medallion" aria-hidden="true">
            <span class="${properties.kcWebAuthnDefaultIcon!}"></span>
        </span>
        <span id="pf-passkey-title" aria-live="polite">${msg("webauthn-login-title")}</span>
    <#elseif section = "form">

        <form id="webauth" action="${url.loginAction}" method="post">
            <input type="hidden" id="clientDataJSON" name="clientDataJSON"/>
            <input type="hidden" id="authenticatorData" name="authenticatorData"/>
            <input type="hidden" id="signature" name="signature"/>
            <input type="hidden" id="credentialId" name="credentialId"/>
            <input type="hidden" id="userHandle" name="userHandle"/>
            <input type="hidden" id="error" name="error"/>
        </form>

        <#if authenticators??>
            <form id="authn_select" class="${properties.kcFormClass!}">
                <#list authenticators.authenticators as authenticator>
                    <input type="hidden" name="authn_use_chk" value="${authenticator.credentialId}"/>
                </#list>
            </form>
        </#if>

        <div class="pf-passkey">
            <#-- Rendered in the resting state, not the waiting one, even though the
                 script flips it to waiting immediately: a browser that never runs
                 the module would otherwise be left looking at a spinner for a
                 prompt that is never going to open. -->
            <p class="pf-passkey-body" id="pf-passkey-body" aria-live="polite">${msg("webauthn-login-body")}</p>

            <div class="${properties.kcFormButtonsClass!}">
                <#-- Not type="submit": #webauth is submitted by webauthnAuthenticate.js
                     once the ceremony resolves, and this button sits outside it. -->
                <button type="button" autofocus
                        class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                        id="authenticateWebAuthnButton">
                    <span class="pf-spinner" aria-hidden="true"></span>
                    <span id="pf-passkey-cta">${msg("webauthn-doAuthenticate")}</span>
                </button>
            </div>
        </div>

        <script type="module">
            <#outputformat "JavaScript">
            // doAuthenticate rather than authenticateByWebAuthn: the wrapper posts
            // every rejection straight back to Keycloak as a failed ceremony, and an
            // auto-started prompt has one rejection that is not one — see below.
            import { doAuthenticate, getAllowCredentials, returnSuccess, returnFailure }
                from "${url.resourcesPath}/js/webauthnAuthenticate.js";

            const input = {
                isUserIdentified : ${isUserIdentified},
                challenge : ${challenge?c},
                userVerification : ${userVerification?c},
                rpId : ${rpId?c},
                createTimeout : ${createTimeout?c},
                errmsg : ${msg("webauthn-unsupported-browser-text")?c}
            };

            const button = document.getElementById('authenticateWebAuthnButton');
            const title = document.getElementById('pf-passkey-title');
            const body = document.getElementById('pf-passkey-body');
            const cta = document.getElementById('pf-passkey-cta');

            const resting = { title: title.textContent, body: body.textContent, cta: cta.textContent };
            const waiting = {
                title: ${msg("webauthn-login-waiting-title")?c},
                body: ${msg("webauthn-login-waiting-body")?c},
                cta: ${msg("webauthn-login-waiting-cta")?c}
            };

            // A browser that refuses to open the prompt at all — no user activation
            // on the document — rejects with the same NotAllowedError a user gets
            // for dismissing it, so the name alone cannot tell the two apart. What
            // can is the clock: a refusal comes back before the prompt could have
            // been drawn, a dismissal takes as long as a person takes.
            const REFUSAL_MS = 400;

            let running = false;

            function setWaiting(isWaiting) {
                const state = isWaiting ? waiting : resting;
                title.textContent = state.title;
                body.textContent = state.body;
                cta.textContent = state.cta;
                button.disabled = isWaiting;
                if (isWaiting) {
                    button.setAttribute('aria-busy', 'true');
                } else {
                    button.removeAttribute('aria-busy');
                }
            }

            async function authenticate(auto) {
                if (running) {
                    return;
                }
                running = true;
                setWaiting(true);

                const startedAt = Date.now();
                try {
                    const result = await doAuthenticate({
                        ...input,
                        allowCredentials: input.isUserIdentified ? getAllowCredentials() : []
                    });
                    // Undefined means doAuthenticate already posted the
                    // unsupported-browser error and this page is on its way out.
                    if (result) {
                        returnSuccess(result);
                    }
                } catch (error) {
                    if (auto && error && error.name === 'NotAllowedError' && Date.now() - startedAt < REFUSAL_MS) {
                        // Nobody was asked anything, so there is nothing to report as
                        // a failure. Hand the page back with its button.
                        running = false;
                        setWaiting(false);
                        return;
                    }
                    returnFailure(error);
                }
            }

            button.addEventListener('click', () => authenticate(false));

            // A background tab cannot open the prompt, and firing one at a tab the
            // user is not looking at would be a prompt they never asked for. Leave
            // those on the button.
            if (document.visibilityState === 'visible') {
                authenticate(true);
            }
            </#outputformat>
        </script>

    </#if>
</@layout.registrationLayout>
