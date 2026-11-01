<#--
  The realm has no passwords, so its browser flow asks for an email address and
  nothing else — this page is the branded version of that step. The credential
  itself (a passkey, or a code mailed to the address) is collected afterwards.
-->
<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username') displayInfo=realm.registrationAllowed && !registrationDisabled??; section>
    <#if section = "header">
        <h1 class="pf-title">${msg("loginAccountTitle")}</h1>
        <p class="pf-subtitle">Welcome back. Pick up where you left off.</p>
    <#elseif section = "form">
        <form id="kc-form-login" onsubmit="login.disabled = true; return true;" action="${url.loginAction}" method="post" class="${properties.kcFormClass!}">
            <#if !usernameHidden??>
              <div class="${properties.kcFormGroupClass!}">
                <label for="username" class="${properties.kcLabelClass!}"><#if !realm.loginWithEmailAllowed>${msg("username")}<#elseif !realm.registrationEmailAsUsername>${msg("usernameOrEmail")}<#else>${msg("email")}</#if></label>
                <#-- webauthn is in the autocomplete list so a browser holding a passkey for
                     this site can offer it inline, without the user asking for it first. -->
                <input tabindex="1" id="username" class="${properties.kcInputClass!}" name="username" value="${(login.username!'')}" type="text" autofocus autocomplete="username webauthn"
                       aria-invalid="<#if messagesPerField.existsError('username')>true</#if>" />
                <#if messagesPerField.existsError('username')>
                  <span id="input-error" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
                    ${kcSanitize(messagesPerField.getFirstError('username'))?no_esc}
                  </span>
                </#if>
              </div>
            </#if>

            <div class="${properties.kcFormOptionsClass!}">
              <#if realm.rememberMe && !usernameHidden??>
                <label class="pf-checkbox-label">
                  <input tabindex="3" id="rememberMe" name="rememberMe" type="checkbox" class="${properties.kcCheckboxInputClass!}" <#if login.rememberMe??>checked</#if>>
                  ${msg("rememberMe")}
                </label>
              </#if>
            </div>

            <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>/>
            <button tabindex="4" class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}" name="login" id="kc-login" type="submit">${msg("doContinue")}</button>
        </form>
    <#elseif section = "socialProviders">
      <#if social?? && social.providers?has_content>
        <div class="pf-social-wrap">
          <div class="pf-divider"><span>${msg("identity-provider-login-label")}</span></div>
          <ul class="${properties.kcFormSocialAccountListClass!}">
            <#list social.providers as p>
              <li>
                <a id="social-${p.alias}" href="${p.loginUrl}" class="${properties.kcFormSocialAccountListButtonClass!}">
                  <span class="${properties.kcFormSocialAccountNameClass!}">${p.displayName!}</span>
                </a>
              </li>
            </#list>
          </ul>
        </div>
      </#if>
    <#elseif section = "info">
      <#if realm.registrationAllowed && !registrationDisabled??>
        <span>${msg("noAccount")} <a tabindex="6" href="${url.registrationUrl}" class="pf-link">${msg("doRegister")}</a></span>
      </#if>
    </#if>
</@layout.registrationLayout>
