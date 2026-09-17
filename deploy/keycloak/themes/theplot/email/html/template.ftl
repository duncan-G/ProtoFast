<#--
  Reel & Ink, rendered for email.

  Tokens are the same values as clients/theplot/src/styles/nocturne.css and
  infra/keycloak/themes/theplot/login/resources/css/styles.css — but written
  out as literal hex, because an email cannot use CSS custom properties,
  color-mix() or an external stylesheet. Where the design system composes a
  colour (`color-mix(in srgb, #e9e9ed 16%, transparent)` over the surface), the
  flattened result is spelled out with the source noted beside it.

  The house rules this file keeps, from the same system the login theme follows:
  outlined primary actions (never a filled accent), accent as line and glow
  rather than flood, headings no heavier than 500, flush-left layout, and rules
  that fade to transparent at their ends.

  Email-client constraints that shape the markup:
  - Tables and inline styles, not divs and classes. The <style> block is for
    what inlining cannot express: the mobile media query, and the prose of the
    INHERITED base templates (org-invite, identity-provider-link, the event-*
    notifications) whose <p>/<a> markup this theme never sees.
  - The brand mark is drawn with table cells rather than an SVG or an <img>:
    Gmail drops inline SVG and blocks data: URIs, and there is no asset host
    this theme could point an <img> at. So the book is two coloured cells with
    the page furniture inside them, not the curve the SVG draws.
  - The lockup is NOT a link. On the auth pages it points at client.baseUrl; the
    email model has no equivalent, and the Keycloak host's own "/" is the admin
    welcome page. An unlinked mark is the same fallback login/template.ftl takes.
  - This is a dark email by design. Clients that force-invert do so for LIGHT
    mail; a dark ground is generally left alone, and the meta color-scheme tags
    below tell the ones that ask.
-->

<#--
  Reel & Ink token values, prefixed n* so they never collide with the pf* macro
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
<#assign nFont = "Inter,-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif">

<#assign nBg        = "#1e1811"><#-- --color-bg -->
<#assign nSurface   = "#2b251f"><#-- --color-surface -->
<#assign nInk       = "#ece9e4"><#-- --color-text -->
<#assign nAccent    = "#d29843"><#-- --color-accent -->
<#assign nHelper    = "#a29688"><#-- --color-neutral-500 -->
<#assign nFootnote  = "#83786b"><#-- --color-neutral-600 -->
<#-- --color-divider, `color-mix(in srgb, #ece9e4 16%, transparent)`, flattened
     against the card surface it is always drawn on. -->
<#assign nDivider   = "#4a443f">
<#-- The card carries --shadow-md on the web: a 1px #655c51 ring PLUS an ambient
     drop shadow. Email has no ambient shadow, and that ring alone reads harsher
     than the real card, so the edge steps down to --color-neutral-800. -->
<#assign nCardEdge  = "#484139">
<#-- The book mark's three values: the two pages and the furniture on them. -->
<#assign nPageBack  = "#a26e18"><#-- --color-accent-600 -->
<#assign nPageFront = "#c48c38"><#-- --color-accent-500 -->
<#assign nPageInk   = "#f7e5ce"><#-- --color-accent-200 -->

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
         style="width:100%;border-collapse:collapse;background-color:${nBg};background-image:radial-gradient(62rem 36rem at 50% -6rem, #3b2c19, ${nBg} 70%);">
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
                style="background-color:${nSurface};border:1px solid ${nCardEdge};border-radius:14px;padding:32px;">
              <#if heading?has_content>
              <h1 class="pf-h1" style="margin:0 0 14px;font-family:${nFont};font-size:26px;font-weight:500;line-height:1.12;letter-spacing:-0.5px;color:${nInk};">${heading}</h1>
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
  The ThePlot lockup: the book mark plus the wordmark — the same lockup as
  login/template.ftl's pfBrandLockup and
  clients/theplot/src/app/shared/app-shell.ts.

  Drawn in table cells because email clients drop SVG, so the two pages are two
  coloured cells with a 2px gutter between them rather than the curved leaves the
  SVG draws. What survives the translation is what the mark is about: lines to
  read on the left page, a triangle to press on the right.

  The bars are <div>s inside one cell rather than cells of their own: a table
  gives every cell in a column the same width, which would stretch the short bar
  to the width of the long one.
-->
<#macro pfBrandLockup>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
  <tr>
    <td valign="middle" style="padding:0 9px 0 0;font-size:0;line-height:0;">
      <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
        <tr>
          <#-- Left page: the lines you read. The vertical padding is what makes
               the cell portrait rather than square — a page is taller than it is
               wide, and without that the two cells read as two buttons. -->
          <td width="13" valign="middle" bgcolor="${nPageBack}"
              style="width:13px;padding:8px 3px;background-color:${nPageBack};border-radius:2px 0 0 2px;font-size:0;line-height:0;">
            <div style="width:7px;height:2px;line-height:2px;font-size:0;background-color:${nPageInk};border-radius:1px;">&nbsp;</div>
            <div style="height:3px;line-height:3px;font-size:0;">&nbsp;</div>
            <div style="width:4px;height:2px;line-height:2px;font-size:0;background-color:${nPageInk};border-radius:1px;">&nbsp;</div>
          </td>
          <#-- The gutter, which is what makes the two blocks read as one book. -->
          <td width="2" style="width:2px;font-size:0;line-height:0;">&nbsp;</td>
          <#-- Right page: the triangle you press. A zero-size div with borders is
               the only triangle email has; Outlook's Word engine drops it and
               leaves a plain page, which still reads as the other half of the
               book. An image would be blocked more often than it would render. -->
          <td width="13" align="center" valign="middle" bgcolor="${nPageFront}"
              style="width:13px;padding:8px 3px;background-color:${nPageFront};border-radius:0 2px 2px 0;font-size:0;line-height:0;">
            <div style="width:0;height:0;font-size:0;line-height:0;border-top:4px solid transparent;border-bottom:4px solid transparent;border-left:6px solid ${nPageInk};"></div>
          </td>
        </tr>
      </table>
    </td>
    <td valign="middle" style="padding:0;font-family:${nFont};font-size:17px;font-weight:500;letter-spacing:-0.34px;color:${nInk};">${realmName!'ThePlot'}</td>
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
  The primary action. Reel & Ink outlines it — an accent border on transparent,
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
  A Reel & Ink rule: fades to transparent at both ends instead of stopping cleanly.
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
