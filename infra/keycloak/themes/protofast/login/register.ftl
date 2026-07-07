<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=messagesPerField.exists('global') displayRequiredFields=false; section>
    <#if section = "header">
        <h1 class="pf-title">${msg("registerTitle")}</h1>
        <p class="pf-subtitle">Start building — your first prototype is free.</p>
    <#elseif section = "form">
        <form id="kc-register-form" action="${url.registrationAction}" method="post" class="${properties.kcFormClass!}">
            <div class="${properties.kcFormGroupClass!}">
                <label for="email" class="${properties.kcLabelClass!}">${msg("email")}</label>
                <input type="email" id="email" name="email" value="${(register.formData.email!'')}" autocomplete="email" autofocus
                       class="${properties.kcInputClass!}" aria-invalid="<#if messagesPerField.existsError('email')>true</#if>" />
                <#if messagesPerField.existsError('email')>
                    <span id="input-error-email" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
                        ${kcSanitize(messagesPerField.get('email'))?no_esc}
                    </span>
                </#if>
            </div>

            <#if passwordRequired??>
                <div class="${properties.kcFormGroupClass!}">
                    <label for="password" class="${properties.kcLabelClass!}">${msg("password")}</label>
                    <input type="password" id="password" name="password" autocomplete="new-password"
                           class="${properties.kcInputClass!}" aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>" />
                    <#if messagesPerField.existsError('password')>
                        <span id="input-error-password" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
                            ${kcSanitize(messagesPerField.get('password'))?no_esc}
                        </span>
                    </#if>
                </div>

                <div class="${properties.kcFormGroupClass!}">
                    <label for="password-confirm" class="${properties.kcLabelClass!}">${msg("passwordConfirm")}</label>
                    <input type="password" id="password-confirm" name="password-confirm" autocomplete="new-password"
                           class="${properties.kcInputClass!}" aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>" />
                    <#if messagesPerField.existsError('password-confirm')>
                        <span id="input-error-password-confirm" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
                            ${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}
                        </span>
                    </#if>
                </div>
            </#if>

            <#if recaptchaRequired??>
                <div class="${properties.kcFormGroupClass!}">
                    <div class="g-recaptcha" data-size="compact" data-sitekey="${recaptchaSiteKey}"></div>
                </div>
            </#if>

            <button class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}" type="submit">${msg("doRegister")}</button>

            <p class="pf-info">
                <a href="${url.loginUrl}" class="pf-link">${kcSanitize(msg("backToLogin"))?no_esc}</a>
            </p>
        </form>
    </#if>
</@layout.registrationLayout>
