<#--
  Ember, rendered for email.

  Tokens are the same values as clients/theplot/src/styles/ember.css and
  infra/keycloak/themes/theplot/login/resources/css/styles.css — but written
  out as literal hex, because an email cannot use CSS custom properties,
  color-mix() or an external stylesheet. Where the design system composes a
  colour (`color-mix(in srgb, #f0e6d7 12%, transparent)` over the surface), the
  flattened result is spelled out with the source noted beside it.

  The house rules this file keeps, from the same system the login theme follows:
  a filled accent pill for the primary action, hairline outlines elsewhere,
  serif headings with the sans body, square surfaces, flush-left layout.

  Email-client constraints that shape the markup:
  - Tables and inline styles, not divs and classes. The <style> block is for
    what inlining cannot express: the mobile media query, and the prose of the
    INHERITED base templates (org-invite, identity-provider-link, the event-*
    notifications) whose <p>/<a> markup this theme never sees.
  - The brand mark is drawn with table cells rather than an SVG or an <img>:
    Gmail drops inline SVG and blocks data: URIs, and there is no asset host
    this theme could point an <img> at.
  - The lockup is NOT a link. On the auth pages it points at client.baseUrl; the
    email model has no equivalent, and the Keycloak host's own "/" is the admin
    welcome page. An unlinked mark is the same fallback login/template.ftl takes.
  - This is a dark email by design. Clients that force-invert do so for LIGHT
    mail; a dark ground is generally left alone, and the meta color-scheme tags
    below tell the ones that ask.
-->

<#--
  Ember token values, prefixed n* so they never collide with the pf* macro
  names below — FreeMarker keeps variables and macros in the one namespace.

  Two spellings differ from the source sheet on purpose:
  - Font families are UNQUOTED. FreeMarker's HTML output format escapes ' to
    &#39;; an attribute value decodes that back to a quote but a <style> element
    does not, so a quoted stack would break the rules in <style>. `Segoe UI`
    unquoted is valid CSS — every word is a bare identifier. Inter itself is
    rarely installed and webfonts load in few mail clients, so the system stack
    behind it is what most recipients actually see.
  - Composed colours are pre-flattened, since email has no color-mix().
-->
<#assign nFont = "Geist,-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif">
<#assign nSerif = "Newsreader,Georgia,Times New Roman,serif">

<#assign nBg        = "#14110f"><#-- --color-bg -->
<#assign nSurface   = "#1d1916"><#-- --color-surface -->
<#assign nInk       = "#f1ebe2"><#-- --color-text -->
<#assign nAccent    = "#e8794a"><#-- oklch(0.72 0.15 45) flattened --><#-- --color-accent -->
<#assign nHelper    = "#a89f93"><#-- --color-neutral-500 -->
<#assign nFootnote  = "#8a8076"><#-- --color-neutral-600 -->
<#-- --color-divider, `color-mix(in srgb, #f0e6d7 12%, transparent)`, flattened
     against the card surface it is always drawn on. -->
<#assign nDivider   = "#3a352e">
<#-- The card carries --shadow-md on the web: a 1px #3a342c ring PLUS an ambient
     drop shadow. Email has no ambient shadow, and that ring alone reads harsher
     than the real card, so the edge steps down to --color-neutral-800. -->
<#assign nCardEdge  = "#46403a">

<#macro emailLayout heading="" preheader="">
<!DOCTYPE html>
<html lang="${(locale.language)!'en'}" dir="${((ltr)!true)?then('ltr','rtl')}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="x-apple-disable-message-reformatting">
  <meta name="color-scheme" content="dark">
  <meta name="supported-color-schemes" content="dark">
  <title><#if heading?has_content>${heading}<#else>${realmName!'ThePlot'}</#if></title>
  <#-- What this block is for, kept OUT of it: CSS comments ship with every
       message, and angle brackets inside a <style> element are one more thing
       for a mail client's sanitiser to trip over.

       .pf-prose  — the INHERITED base templates (org-invite,
                    identity-provider-link, the event-* notices) emit bare
                    p/a/b markup this theme never gets to style inline. Outlook's
                    Word engine honours simple class selectors, so the prose
                    lands on-palette there too.
       a[x-apple-data-detectors] — Apple Mail autolinks dates and addresses and
                    repaints them its own blue, which is unreadable on this ground.
       @media     — the card and the heading are the only things that need to
                    give on a phone. -->
  <style>
    .pf-prose p { margin: 0 0 12px; font-family: ${nFont}; font-size: 15px; line-height: 1.55; color: ${nInk}; }
    .pf-prose p:last-child { margin-bottom: 0; }
    .pf-prose a { color: ${nAccent}; text-decoration: underline; }
    .pf-prose b, .pf-prose strong { font-weight: 600; color: ${nInk}; }
    a { color: ${nAccent}; }
    a[x-apple-data-detectors] { color: inherit !important; text-decoration: none !important; }
    @media only screen and (max-width: 600px) {
      .pf-card { padding: 24px 20px !important; }
      .pf-h1 { font-size: 22px !important; }
    }
  </style>
</head>
<body style="margin:0;padding:0;width:100%;background-color:${nBg};">
  <#-- Inbox preview line. Hidden in the body itself; the zero-width joiners stop
       clients padding the preview out with whatever markup follows. -->
  <#if preheader?has_content>
  <div style="display:none;font-size:0;line-height:0;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;">${preheader}&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;</div>
  </#if>

  <#-- The radial is the same accent bloom the landing page's closing CTA and the
       login page's .pf-glow use. Clients without gradient support fall back to
       the flat bgcolor, which is the design's own ground — nothing looks broken. -->
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="${nBg}"
         style="width:100%;border-collapse:collapse;background-color:${nBg};background-image:radial-gradient(62rem 36rem at 50% -6rem, #2e1c10, ${nBg} 70%);">
    <tr>
      <td align="center" style="padding:32px 16px 40px;">
        <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" style="width:560px;max-width:100%;border-collapse:collapse;">

          <#-- Brand -->
          <tr>
            <td align="left" style="padding:0 4px 18px;"><@pfBrandLockup/></td>
          </tr>

          <#-- Card -->
          <tr>
            <td class="pf-card" bgcolor="${nSurface}" align="left"
                style="background-color:${nSurface};border:1px solid ${nCardEdge};padding:32px;">
              <#if heading?has_content>
              <h1 class="pf-h1" style="margin:0 0 14px;font-family:${nSerif};font-size:28px;font-weight:400;line-height:1.1;letter-spacing:-0.5px;color:${nInk};">${heading}</h1>
              </#if>
              <div class="pf-prose"><#nested></div>
            </td>
          </tr>

          <#-- Footer -->
          <tr>
            <td align="left" style="padding:18px 4px 0;font-family:${nFont};font-size:12.5px;line-height:1.5;color:${nFootnote};">
              <#if (user.email)??>
                ${msg("pfFooterSentTo", user.email)}<br>
              </#if>
              &copy; ${.now?string('yyyy')} ${realmName!'ThePlot'}. ${msg("pfFooterTagline")}
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>
</#macro>

<#--
  The ThePlot lockup, from the design system: the serif wordmark — italic
  "The" against "Plot" — closed with the plot-point, a small square of the
  accent. The same lockup as login/template.ftl's pfBrandLockup and
  clients/theplot/src/app/shared/theplot-logo.ts.

  The dot is a background-coloured cell rather than an image or glyph: a
  square renders identically in every client, Outlook's Word engine included.
-->
<#macro pfBrandLockup>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
  <tr>
    <td valign="baseline" style="padding:0;font-family:${nSerif};font-size:22px;line-height:1;color:${nInk};"><em style="font-style:italic;font-weight:400;">The</em><span style="font-weight:500;">Plot</span></td>
    <td valign="middle" style="padding:0 0 0 4px;font-size:0;line-height:0;">
      <div style="width:6px;height:6px;line-height:6px;font-size:0;background-color:${nAccent};">&nbsp;</div>
    </td>
  </tr>
</table>
</#macro>

<#-- Body copy. -->
<#macro pfText>
<p style="margin:0 0 12px;font-family:${nFont};font-size:15px;line-height:1.55;color:${nInk};"><#nested></p>
</#macro>

<#-- Secondary line — expiry, "you can ignore this", and the like. -->
<#macro pfNote>
<p style="margin:0 0 10px;font-family:${nFont};font-size:13px;line-height:1.5;color:${nHelper};"><#nested></p>
</#macro>

<#--
  The primary action. Ember outlines it — an accent border on transparent,
  never a filled accent — so do not "fix" this into a solid button.

  Padding sits on the <td>, not on the <a>: Outlook's Word engine ignores
  padding on an inline-block anchor, and the text would sit flush against the
  border. On the td it works in every client.
-->
<#macro pfButton href label>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:separate;margin:18px 0 16px;">
  <tr>
    <td align="center" bgcolor="${nSurface}" style="background-color:${nSurface};border:1px solid ${nAccent};border-radius:8px;padding:12px 22px;">
      <a href="${href}" target="_blank" style="display:inline-block;font-family:${nFont};font-size:15px;font-weight:500;line-height:1.2;color:${nAccent};text-decoration:none;">${label}</a>
    </td>
  </tr>
</table>
</#macro>

<#--
  Every action email needs a copy-pasteable URL: buttons get stripped, and
  corporate mail gateways rewrite them into something a recipient will not trust.
-->
<#macro pfFallback href>
<p style="margin:0 0 6px;font-family:${nFont};font-size:12.5px;line-height:1.5;color:${nFootnote};">${msg("pfFallbackIntro")}</p>
<p style="margin:0;font-family:${nFont};font-size:12.5px;line-height:1.5;word-break:break-all;"><a href="${href}" target="_blank" style="color:${nAccent};text-decoration:underline;">${href}</a></p>
</#macro>

<#--
  A one-time code, set in the system's mono face. Sits on --color-bg so it reads
  as a well cut into the card, the way an .input does on the auth pages.
-->
<#macro pfCode code>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:separate;margin:18px 0;">
  <tr>
    <td align="center" bgcolor="${nBg}" style="background-color:${nBg};border:1px solid ${nDivider};border-radius:8px;padding:14px 22px;font-family:ui-monospace,Menlo,Monaco,Cascadia Code,Courier New,monospace;font-size:24px;font-weight:500;line-height:1.2;letter-spacing:5px;color:${nInk};">${code}</td>
  </tr>
</table>
</#macro>

<#--
  An Ember rule (kept from Nocturne): fades to transparent at both ends instead of stopping cleanly.
  Painted as a background so the fade spans the full width; the flat fallback
  colour keeps a plain hairline in clients with no gradient support.
-->
<#macro pfRule>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;border-collapse:collapse;margin:20px 0 16px;">
  <tr>
    <td height="1" style="height:1px;font-size:0;line-height:1px;background-color:${nDivider};background-image:linear-gradient(to right, ${nSurface}, ${nDivider} 12%, ${nDivider} 88%, ${nSurface});">&nbsp;</td>
  </tr>
</table>
</#macro>
