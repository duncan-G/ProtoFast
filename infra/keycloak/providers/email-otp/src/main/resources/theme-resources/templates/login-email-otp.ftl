<#--
  The mailed-code step of the browser flow.

  `template.ftl` is resolved through the theme chain rather than from this JAR, so the
  page renders inside whichever login theme the realm is using and inherits its classes.
  Two forms, not one: the second posts a resend and must not carry the code field.
-->
<#import "template.ftl" as layout>
<#import "otp-resend.ftl" as resend>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('code'); section>
    <#if section = "header">
        <h1 class="pf-title">${msg("pfOtpSignInTitle")}</h1>
        <p class="pf-subtitle">${msg("pfOtpSignInSubtitle", otpEmail)}</p>
    <#elseif section = "form">
        <form id="kc-email-otp-form" action="${url.loginAction}" method="post" class="${properties.kcFormClass!}">
            <div class="${properties.kcFormGroupClass!}">
                <label for="code" class="${properties.kcLabelClass!}">${msg("pfOtpLabel")}</label>
                <#-- one-time-code lets the OS offer the code straight from the notification -->
                <input id="code" name="code" type="text" class="${properties.kcInputClass!} pf-otp-input"
                       inputmode="numeric" autocomplete="one-time-code" maxlength="${otpCodeLength}"
                       autofocus aria-invalid="<#if messagesPerField.existsError('code')>true</#if>" />
                <p class="${properties.kcInputHelperTextClass!}">${msg("pfOtpHelp")}</p>
            </div>

            <button class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                    name="login" id="kc-otp-submit" type="submit">${msg("pfOtpSubmit")}</button>
        </form>

        <@resend.form id="kc-email-otp-resend"/>
    </#if>
</@layout.registrationLayout>
