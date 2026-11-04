<#--
  Overrides base/login/passkeys.ftl — the block base's login-username.ftl drops
  under the email field to arm conditional (autofill) WebAuthn. Ours keeps the
  hidden forms and the conditional-UI call and drops base's "Use passkey"
  button.

  The button asked the user to choose a credential they have not been asked
  about yet: this flow is email-first, and pressing Continue lands on
  webauthn-authenticate.ftl, which opens the passkey prompt by itself. So the
  button offered nothing the very next screen does not do unprompted, on a page
  whose one job is to collect an address.

  Nothing else is lost with it. initAuthenticate() below is what makes a saved
  passkey appear in the browser's own autofill on the email field, and it runs
  regardless; the button was only a second, manual way in.
-->
<#macro conditionalUIData>
    <#if enableWebAuthnConditionalUI?has_content>
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
        <script type="module">
           <#outputformat "JavaScript">
           import { initAuthenticate } from "${url.resourcesPath}/js/passkeysConditionalAuth.js";

           const args = {
               isUserIdentified : ${isUserIdentified},
               challenge : ${challenge?c},
               userVerification : ${userVerification?c},
               rpId : ${rpId?c},
               createTimeout : ${createTimeout?c},
               mediation : ${(mediation!'conditional')?c},
               authenticatorAttachment : ${authenticatorAttachment?c},
           };

           document.addEventListener("DOMContentLoaded", (event) => initAuthenticate({errmsg : ${msg("passkey-unsupported-browser-text")?c}, ...args}));
           </#outputformat>
        </script>
    </#if>
</#macro>
