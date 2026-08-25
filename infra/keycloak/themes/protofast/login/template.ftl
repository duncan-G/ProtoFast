<#--
  The Protofast lockup: the swarm mark (five parallel strokes resolving into
  one solid dot) plus the wordmark — the same lockup the app renders in
  clients/protofast/src/app/shared/protofast-logo.ts. Path data is copied from
  there verbatim; if one changes, change both.

  Inline SVG rather than an <img> so the wordmark can sit on the same baseline
  and take the hover colour. Strokes and the dot read from the Nocturne tokens
  so the mark stays on-palette with the rest of the page.
-->
<#macro pfBrandLockup>
  <svg class="pf-brand-mark" viewBox="0 0 32 32" fill="none" aria-hidden="true">
    <g stroke-width="2.8" stroke-linecap="round">
      <path d="M5 6 H13" stroke="var(--color-accent-800)"/>
      <path d="M5 11 H16" stroke="var(--color-accent-700)"/>
      <path d="M5 16 H19" stroke="var(--color-accent-600)"/>
      <path d="M5 21 H16" stroke="var(--color-accent-700)"/>
      <path d="M5 26 H13" stroke="var(--color-accent-800)"/>
    </g>
    <circle cx="25" cy="16" r="4" fill="var(--color-accent)"/>
  </svg><span>Protofast</span></#macro>

<#macro registrationLayout displayInfo=false displayMessage=true displayRequiredFields=false showAnotherWayIfPresent=true>
<!DOCTYPE html>
<html class="${properties.kcHtmlClass!}"<#if realm.internationalizationEnabled> lang="${locale.currentLanguageTag}"</#if>>

<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>${msg("loginTitle",(realm.displayName!''))}</title>
  <#-- Inter is Nocturne's --font-heading and --font-body; the app loads it the
       same way. Self-host under resources/fonts/ if the third-party request on
       the auth page ever becomes a concern. -->
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet">
  <#-- Same mark the app ships at clients/protofast/public/favicon.svg, so the tab
       icon does not change when the browser hands off to the auth host. -->
  <link rel="icon" type="image/svg+xml" href="${url.resourcesPath}/img/favicon.svg">
  <#if properties.styles?has_content>
    <#list properties.styles?split(' ') as style>
      <link href="${url.resourcesPath}/${style}" rel="stylesheet">
    </#list>
  </#if>
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
