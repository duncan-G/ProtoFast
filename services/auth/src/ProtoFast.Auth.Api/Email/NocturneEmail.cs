using System.Net;
using System.Text;

namespace ProtoFast.Auth.Api.Email;

/// <summary>
/// Nocturne, rendered for the mail auth-svc sends on its own account.
///
/// <para>The HTML side of an <see cref="EmailMessage"/>. Deliberately a near-transcription of
/// <c>infra/keycloak/themes/protofast/email/html/template.ftl</c>: the two senders write into the
/// same inboxes, and a sign-in code from Keycloak sitting next to an email-change code from here
/// should look like the same product wrote both. Change one, change the other — the token values,
/// the card geometry and the brand lockup are the parts that have to stay in step.</para>
///
/// <para>The same email-client constraints shape this as shape the theme: tables and inline
/// styles rather than divs and classes, literal hex instead of the custom properties in
/// clients/protofast/src/styles/nocturne.css, and the brand mark drawn in table cells because
/// Gmail drops inline SVG and blocks data: URIs. The house rules are the design system's:
/// outlined actions, accent as line and glow rather than flood, headings no heavier than 500,
/// flush-left layout, rules that fade out at their ends.</para>
/// </summary>
internal static class NocturneEmail
{
    // Unquoted on purpose — a quoted family name is fine in an attribute but the <style> block
    // below takes the same string, and there the quotes would have to survive escaping.
    private const string Font = "Inter,-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif";
    private const string Mono = "ui-monospace,Menlo,Monaco,Cascadia Code,Courier New,monospace";

    private const string Bg = "#161826";        // --color-bg
    private const string Surface = "#232532";   // --color-surface
    private const string Ink = "#e9e9ed";       // --color-text
    private const string Accent = "#9184d9";    // --color-accent
    private const string Helper = "#9397ab";    // --color-neutral-500
    private const string Footnote = "#75798c";  // --color-neutral-600

    /// <summary>--color-divider, <c>color-mix(in srgb, #e9e9ed 16%, transparent)</c>, flattened
    /// against the card surface it is always drawn on. Email has no color-mix().</summary>
    private const string Divider = "#434450";

    /// <summary>The card carries --shadow-md on the web — a 1px #595d6c ring plus an ambient drop
    /// shadow. Email has no ambient shadow and the ring alone reads harsher than the real card, so
    /// the edge steps down to --color-neutral-800.</summary>
    private const string CardEdge = "#3f424d";

    private const string Brand = "Protofast";

    /// <summary>The tagline under the rule, matching the theme's <c>pfFooterTagline</c>.</summary>
    private const string Tagline = "Built fast, obviously.";

    /// <summary>
    /// Wraps rendered blocks in the shell: ground, brand, card, footer.
    /// </summary>
    /// <param name="preheader">The inbox preview line. Hidden in the body itself; without one the
    /// client pads the preview out with whatever markup comes first.</param>
    /// <param name="sentTo">The recipient, named in the footer the way the theme names
    /// <c>user.email</c> — so a message forwarded out of one inbox still says which it was for.</param>
    /// <param name="blocks">Body content, already rendered by the helpers below.</param>
    public static string Page(string heading, string preheader, string sentTo, int year, params string[] blocks)
    {
        var body = string.Concat(blocks);
        var footerSentTo = string.IsNullOrEmpty(sentTo)
            ? ""
            : $"Sent to {Escape(sentTo)}.<br>";

        // A dark email by design. Clients that force-invert do so for LIGHT mail; a dark ground is
        // generally left alone, and the meta tags tell the ones that ask.
        return $$"""
            <!DOCTYPE html>
            <html lang="en" dir="ltr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="x-apple-disable-message-reformatting">
              <meta name="color-scheme" content="dark">
              <meta name="supported-color-schemes" content="dark">
              <title>{{Escape(heading)}}</title>
              <style>
                a { color: {{Accent}}; }
                a[x-apple-data-detectors] { color: inherit !important; text-decoration: none !important; }
                @media only screen and (max-width: 600px) {
                  .pf-card { padding: 24px 20px !important; }
                  .pf-h1 { font-size: 22px !important; }
                }
              </style>
            </head>
            <body style="margin:0;padding:0;width:100%;background-color:{{Bg}};">
              <div style="display:none;font-size:0;line-height:0;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;">{{Escape(preheader)}}&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;&#8204;</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{{Bg}}"
                     style="width:100%;border-collapse:collapse;background-color:{{Bg}};background-image:radial-gradient(62rem 36rem at 50% -6rem, #2b2b48, {{Bg}} 70%);">
                <tr>
                  <td align="center" style="padding:32px 16px 40px;">
                    <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" style="width:560px;max-width:100%;border-collapse:collapse;">
                      <tr>
                        <td align="left" style="padding:0 4px 18px;">{{BrandLockup}}</td>
                      </tr>
                      <tr>
                        <td class="pf-card" bgcolor="{{Surface}}" align="left"
                            style="background-color:{{Surface}};border:1px solid {{CardEdge}};border-radius:14px;padding:32px;">
                          <h1 class="pf-h1" style="margin:0 0 14px;font-family:{{Font}};font-size:26px;font-weight:500;line-height:1.12;letter-spacing:-0.5px;color:{{Ink}};">{{Escape(heading)}}</h1>
                          {{body}}
                        </td>
                      </tr>
                      <tr>
                        <td align="left" style="padding:18px 4px 0;font-family:{{Font}};font-size:12.5px;line-height:1.5;color:{{Footnote}};">
                          {{footerSentTo}}
                          &copy; {{year}} {{Brand}}. {{Tagline}}
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    /// <summary>Body copy.</summary>
    public static string Text(string text) =>
        $"""<p style="margin:0 0 12px;font-family:{Font};font-size:15px;line-height:1.55;color:{Ink};">{Escape(text)}</p>""";

    /// <summary>A secondary line — expiry, "you can ignore this", and the like.</summary>
    public static string Note(string text) =>
        $"""<p style="margin:0 0 10px;font-family:{Font};font-size:13px;line-height:1.5;color:{Helper};">{Escape(text)}</p>""";

    /// <summary>A one-time code, set in the system's mono face. Sits on --color-bg so it reads as
    /// a well cut into the card, the way an .input does on the auth pages.</summary>
    public static string Code(string code) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:separate;margin:18px 0;">
          <tr>
            <td align="center" bgcolor="{Bg}" style="background-color:{Bg};border:1px solid {Divider};border-radius:8px;padding:14px 22px;font-family:{Mono};font-size:24px;font-weight:500;line-height:1.2;letter-spacing:5px;color:{Ink};">{Escape(code)}</td>
          </tr>
        </table>
        """;

    /// <summary>A Nocturne rule: fades to transparent at both ends instead of stopping cleanly.
    /// Painted as a background so the fade spans the full width; the flat colour keeps a plain
    /// hairline in clients with no gradient support.</summary>
    public static string Rule() =>
        $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;border-collapse:collapse;margin:20px 0 16px;">
          <tr>
            <td height="1" style="height:1px;font-size:0;line-height:1px;background-color:{Divider};background-image:linear-gradient(to right, {Surface}, {Divider} 12%, {Divider} 88%, {Surface});">&nbsp;</td>
          </tr>
        </table>
        """;

    /// <summary>
    /// The Protofast lockup: the swarm mark — five parallel strokes resolving into one solid dot —
    /// beside the wordmark, the same lockup as the login theme's <c>pfBrandLockup</c> and
    /// clients/protofast/src/app/shared/protofast-logo.ts.
    ///
    /// <para>Drawn in table cells because mail clients drop SVG. Geometry is the 32-unit viewBox
    /// scaled by 0.75: strokes of width 2.8 at y 6/11/16/21/26 running from x=5 to x=13/16/19/16/13
    /// become 2px bars of width 6/8/10/8/6 on a 4px pitch, and the r=4 dot at cx=25 becomes a 6px
    /// dot middle-aligned beside them. Each bar is a div in its own row rather than a sized td: a
    /// table would give every cell in the column the width of the longest bar.</para>
    ///
    /// <para>Not a link. On the auth pages the mark points at the client's base URL; there is no
    /// equivalent here, and an unlinked mark is the fallback the login theme takes too.</para>
    /// </summary>
    private static string BrandLockup { get; } =
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
          <tr>
            <td valign="middle" style="padding:0 9px 0 0;font-size:0;line-height:0;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
                <tr>
                  <td valign="middle" style="padding:0;font-size:0;line-height:0;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="border-collapse:collapse;">
                      {Bar(6, "#423a6a")}
                      {BarGap}
                      {Bar(8, "#5d5294")}
                      {BarGap}
                      {Bar(10, "#796cbf")}
                      {BarGap}
                      {Bar(8, "#5d5294")}
                      {BarGap}
                      {Bar(6, "#423a6a")}
                    </table>
                  </td>
                  <td width="6" valign="middle" style="padding:0 0 0 2px;font-size:0;line-height:0;">
                    <div style="width:6px;height:6px;line-height:6px;font-size:0;background-color:{Accent};border-radius:3px;">&nbsp;</div>
                  </td>
                </tr>
              </table>
            </td>
            <td valign="middle" style="padding:0;font-family:{Font};font-size:17px;font-weight:500;letter-spacing:-0.34px;color:{Ink};">{Brand}</td>
          </tr>
        </table>
        """;

    private static string Bar(int width, string colour) =>
        $"""<tr><td style="padding:0;font-size:0;line-height:0;"><div style="width:{width}px;height:2px;line-height:2px;font-size:0;background-color:{colour};border-radius:1px;">&nbsp;</div></td></tr>""";

    private const string BarGap =
        """<tr><td height="2" style="padding:0;height:2px;font-size:0;line-height:2px;">&nbsp;</td></tr>""";

    /// <summary>Everything interpolated into the markup goes through here. The addresses in these
    /// messages are user-supplied, and one of them is mailed to a stranger's inbox.</summary>
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
