<#macro registrationLayout displayInfo=false displayMessage=true displayRequiredFields=false showAnotherWayIfPresent=true>
<!DOCTYPE html>
<html class="${properties.kcHtmlClass!}"<#if realm.internationalizationEnabled> lang="${locale.currentLanguageTag}"</#if>>

<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>${msg("loginTitle",(realm.displayName!''))}</title>
  <#if properties.styles?has_content>
    <#list properties.styles?split(' ') as style>
      <link href="${url.resourcesPath}/${style}" rel="stylesheet">
    </#list>
  </#if>
</head>

<body class="${properties.kcBodyClass!}">
  <div class="pf-glow" aria-hidden="true"></div>

  <main class="${properties.kcLoginClass!}">
    <#-- Brand lockup links back to the app that initiated login (client.baseUrl),
         NOT "/" — on the Keycloak host "/" is the admin welcome page. If the base
         URL is unavailable, render a non-linked mark so we never bounce to Keycloak. -->
    <#assign brandUrl = (client.baseUrl)!"">
    <#if brandUrl?has_content><a href="${brandUrl}" class="pf-brand"><#else><span class="pf-brand"></#if>
      <span class="pf-brand-mark" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
          <path fill-rule="evenodd" d="M14.615 1.595a.75.75 0 0 1 .359.852L12.982 9.75h7.268a.75.75 0 0 1 .548 1.262l-10.5 11.25a.75.75 0 0 1-1.272-.71l1.992-7.302H3.75a.75.75 0 0 1-.548-1.262l10.5-11.25a.75.75 0 0 1 .913-.143Z" clip-rule="evenodd" />
        </svg>
      </span>
      <span class="pf-brand-name">Protofast</span>
    <#if brandUrl?has_content></a><#else></span></#if>

    <section class="${properties.kcFormCardClass!}">
      <header class="pf-card-header">
        <#nested "header">
      </header>

      <#-- Message / alert bar -->
      <#if displayMessage && message?? && (message.summary?? && message.summary != "") && (message.type != 'warning' || !isAppInitiatedAction??)>
        <div class="${properties.kcAlertClass!} pf-alert--${message.type}">
          <span class="pf-alert-icon" aria-hidden="true">
            <#if message.type = 'success'>&#10003;<#elseif message.type = 'error'>&#33;<#else>&#8505;</#if>
          </span>
          <span class="${properties.kcAlertTitleClass!}">${kcSanitize(message.summary)?no_esc}</span>
        </div>
      </#if>

      <div class="${properties.kcContentWrapperClass!}">
        <#nested "form">

        <#if auth?has_content && auth.showTryAnotherWayLink() && showAnotherWayIfPresent>
          <form action="${url.loginAction}" class="pf-form" method="post">
            <input type="hidden" name="tryAnotherWay" value="on"/>
            <a href="#" class="pf-link" onclick="document.forms['kc-select-try-another-way-form'] ? document.forms['kc-select-try-another-way-form'].submit() : this.closest('form').submit(); return false;">${msg("doTryAnotherWay")}</a>
          </form>
        </#if>

        <#nested "socialProviders">

        <#if displayInfo>
          <div class="${properties.kcInfoAreaWrapperClass!} pf-info">
            <#nested "info">
          </div>
        </#if>
      </div>
    </section>

    <p class="pf-footnote">&copy; ${.now?string('yyyy')} Protofast. Built fast, obviously.</p>
  </main>
</body>
</html>
</#macro>
