<#--
  The ThePlot lockup: the book mark plus the wordmark — the same lockup the app
  renders in clients/theplot/src/app/shared/app-shell.ts. An open book whose left
  page carries the lines you read and a caret where you write, and whose right
  page opens into the triangle you press to watch. Path data is copied from there
  verbatim; if one changes, change both.

  Inline SVG rather than an <img> so the wordmark can sit on the same baseline
  and take the hover colour. The pages and the page furniture read from the
  Reel & Ink tokens so the mark stays on-palette with the rest of the page.

  clients/theplot/public/favicon.svg is the reduced cut of this: same silhouette
  without the caret, which at 16px reads as a third line rather than a cursor.
-->
<#macro pfBrandLockup>
  <svg class="pf-brand-mark" viewBox="0 0 32 32" aria-hidden="true">
    <path d="M15 11.2C12.4 9.4 9 8.6 5.4 8.9V22.6C9 22.3 12.4 23.1 15 24.9Z" fill="var(--color-accent-600)"/>
    <path d="M17 11.2C19.6 9.4 23 8.6 26.6 8.9V22.6C23 22.3 19.6 23.1 17 24.9Z" fill="var(--color-accent-500)"/>
    <g stroke="var(--color-accent-200)" stroke-width="1.6" stroke-linecap="round">
      <path d="M8.2 13.9 H12.8"/>
      <path d="M8.2 17.7 H11"/>
    </g>
    <rect x="12.4" y="15.3" width="1.3" height="4.8" rx="0.6" fill="var(--color-accent-100)"/>
    <path d="M20.6 13.4 24.9 16.6 20.6 19.8Z" fill="var(--color-accent-200)" stroke="var(--color-accent-200)" stroke-width="1.1" stroke-linejoin="round"/>
  </svg><span>ThePlot</span></#macro>

<#macro registrationLayout displayInfo=false displayMessage=true displayRequiredFields=false showAnotherWayIfPresent=true>
<!DOCTYPE html>
<html class="${properties.kcHtmlClass!}"<#if realm.internationalizationEnabled> lang="${locale.currentLanguageTag}"</#if>>

<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>${msg("loginTitle",(realm.displayName!''))}</title>
  <#-- Inter is Reel & Ink's --font-heading and --font-body; the app loads it the
       same way. Self-host under resources/fonts/ if the third-party request on
       the auth page ever becomes a concern. -->
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
  <#-- Same mark the app ships at clients/theplot/public/favicon.svg, so the tab
       icon does not change when the browser hands off to the auth host. -->
  <link rel="icon" type="image/svg+xml" href="${url.resourcesPath}/img/favicon.svg">
  <#if properties.styles?has_content>
    <#list properties.styles?split(' ') as style>
      <link href="${url.resourcesPath}/${style}" rel="stylesheet">
    </#list>
  </#if>
  <#-- Passkey offer + naming share one card. .pf-group / .pf-btn set display
       and beat the UA [hidden] rule, which is how both steps paint at once.
       Keycloak serves styles.css from an unhashed /resources/{version}/ URL,
       so these ID rules travel with the HTML. webauthnRegister.js only
       toggles the hidden attribute — it does not need a matching class. -->
  <style>
    #pf-passkey-name[hidden],
    #pf-passkey-save[hidden],
    #pf-passkey-actions[hidden] {
      display: none !important;
    }
  </style>
  <#-- Keycloak's WebAuthn modules (webauthnRegister.js / webauthnAuthenticate.js)
       import the bare specifier "rfc4648". Without this map the module fails to
       load, the Register button — which sits outside the form — does nothing,
       and 1Password never gets a credentials.create() to intercept. Copied
       from base/login/template.ftl. -->
  <script type="importmap">
    {
      "imports": {
        "rfc4648": "${url.resourcesCommonPath}/vendor/rfc4648/rfc4648.js"
      }
    }
  </script>

  <#-- Authenticator SPIs push per-page scripts through this list; base's
       template.ftl is the only thing that renders it, so overriding the
       template drops them. -->
  <#if scripts??>
    <#list scripts as script>
      <script src="${script}" type="text/javascript"></script>
    </#list>
  </#if>

  <#-- The rest of this block is base/login/template.ftl's head scripts, carried
       here because overriding template.ftl replaces base's <head> wholesale
       rather than extending it — the same reason the import map above went
       missing. base's menu-button-links.js is deliberately NOT carried: it only
       drives the locale dropdown keyboard nav, and this theme renders no locale
       switcher (the realm leaves internationalizationEnabled off). -->

  <#-- Signing in on another tab leaves this one sitting on a dead form. Poll for
       the session cookie and follow it across when one appears. -->
  <script type="module">
    <#outputformat "JavaScript">
    import { startSessionPolling } from ${(url.resourcesPath + "/js/authChecker.js")?c};

    startSessionPolling(
      ${url.ssoLoginInOtherTabsUrl?c}
    );
    </#outputformat>
  </script>

  <#-- base's login.ftl and login-username.ftl render their Google/Apple buttons
       as <a data-once-link> and rely on this handler to disable them on click;
       without it a double click fires the identity-provider redirect twice. The
       `passwordless` flow lands on login-passkeys-conditional-authenticate.ftl
       by default, which has no social section — login-username.ftl is the
       fallback when the passkey prompt is skipped. Harmless no-op elsewhere.
       Anchors can't match :disabled, so styles.css keys the dimmed state off the
       aria-disabled attribute this sets. -->
  <script type="module">
    document.addEventListener("click", (event) => {
      const link = event.target.closest("a[data-once-link]");

      if (!link) {
        return;
      }

      if (link.getAttribute("aria-disabled") === "true") {
        event.preventDefault();
        return;
      }

      const { disabledClass } = link.dataset;

      if (disabledClass) {
        link.classList.add(...disabledClass.trim().split(/\s+/));
      }

      link.setAttribute("role", "link");
      link.setAttribute("aria-disabled", "true");
    });
  </script>

  <#-- Restarting the flow in another tab invalidates this page's auth session.
       Reload rather than let the user post into a stale one. -->
  <#if authenticationSession??>
    <script type="module">
      <#outputformat "JavaScript">
      import { checkAuthSession } from ${(url.resourcesPath + "/js/authChecker.js")?c};

      checkAuthSession(
        ${authenticationSession.authSessionIdHash?c}
      );
      </#outputformat>
    </script>
  </#if>
</head>

<body class="${properties.kcBodyClass!}">
  <div class="pf-glow" aria-hidden="true"></div>

  <main class="${properties.kcLoginClass!}">
    <#-- Brand lockup links back to the app that initiated login (client.baseUrl),
         NOT "/" — on the Keycloak host "/" is the admin welcome page. If the base
         URL is unavailable, render a non-linked mark so we never bounce to Keycloak. -->
    <#assign brandUrl = (client.baseUrl)!"">
    <#if brandUrl?has_content><a href="${brandUrl}" class="pf-brand"><@pfBrandLockup/></a><#else><span class="pf-brand"><@pfBrandLockup/></span></#if>

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
          <form id="kc-select-try-another-way-form" action="${url.loginAction}" class="pf-form pf-try-another" method="post">
            <input type="hidden" name="tryAnotherWay" value="on"/>
            <a href="#" class="pf-link" onclick="document.forms['kc-select-try-another-way-form'].submit(); return false;">${msg("doTryAnotherWay")}</a>
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

    <p class="pf-footnote">&copy; ${.now?string('yyyy')} ThePlot. Read it, write it, watch it.</p>
  </main>
</body>
</html>
</#macro>
