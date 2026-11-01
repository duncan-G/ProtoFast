<#--
  The sign-up counterpart of login-email-otp.ftl: same mechanics, different copy,
  because here the code proves a new address rather than opening an existing account.
-->
<#import "template.ftl" as layout>
<#import "otp-resend.ftl" as resend>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('code'); section>
    <#if section = "header">
        <h1 class="pf-title">${msg("pfOtpVerifyTitle")}</h1>
        <p class="pf-subtitle">${msg("pfOtpVerifySubtitle", otpEmail)}</p>
    <#elseif section = "form">
        <form id="kc-verify-email-otp-form" action="${url.loginAction}" method="post" class="${properties.kcFormClass!}">
            <div class="${properties.kcFormGroupClass!}">
                <label for="code" class="${properties.kcLabelClass!}">${msg("pfOtpLabel")}</label>
                <input id="code" name="code" type="text" class="${properties.kcInputClass!} pf-otp-input"
                       inputmode="numeric" autocomplete="one-time-code" maxlength="${otpCodeLength}"
                       autofocus aria-invalid="<#if messagesPerField.existsError('code')>true</#if>" />
                <p class="${properties.kcInputHelperTextClass!}">${msg("pfOtpHelp")}</p>
            </div>

            <button class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
                    name="login" id="kc-verify-otp-submit" type="submit">${msg("pfOtpVerifySubmit")}</button>
        </form>

        <@resend.form id="kc-verify-email-otp-resend"/>
    </#if>
</@layout.registrationLayout>
