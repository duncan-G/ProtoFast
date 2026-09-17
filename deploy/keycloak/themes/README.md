# Keycloak themes (prod)

This directory is the **prod** mount source for Keycloak's custom themes
(`docker-compose.host-services.yml` mounts it at `/opt/keycloak/themes`, read-only).

The canonical, hand-edited theme lives in
[`infra/keycloak/themes/`](../../../infra/keycloak/themes/) (mounted by the dev
Aspire host at `/opt/keycloak/themes`). Keep this copy in sync with it — they are
the same committed theme, staged here so the deploy bundle that syncs `deploy/`
to the host carries the theme without depending on `infra/`.

Two of the login pages this theme styles are **not** in this directory. The
email-code forms ship as theme resources inside the provider JAR
([`../providers/`](../providers/)) so they travel with the authenticator that
renders them; they import `template.ftl` by name, which resolves through the
theme chain, so they still come out wearing this theme. Their copy lives in the
JAR's own message bundle, under `pfOtp*` keys.

There is **one theme per realm**: `protofast` and `theplot`. Each ships two theme
types, both selected in that realm's import (`../realms/<realm>-realm.json`) and
both reconciled onto an already-existing realm by `KC_REALM_KEYS` in
`../../deploy.sh` — so switching a realm's theme needs no manual step:

| type    | realm key    | what it covers |
| ------- | ------------ | -------------- |
| `login` | `loginTheme` | sign-in, sign-up, reset/update password, OTP, verify-email, error pages |
| `email` | `emailTheme` | every message the realm sends |

Each renders its own product's design system, so a realm's mail and auth pages
read as the same product as its app: `protofast` renders Nocturne (see
[`clients/protofast/src/styles/nocturne.css`](../../../clients/protofast/src/styles/nocturne.css)),
`theplot` renders Reel & Ink (see
[`clients/theplot/src/styles/nocturne.css`](../../../clients/theplot/src/styles/nocturne.css)) —
the same geometry on a warm palette with its own brand.

The two themes are **standalone siblings**, not parent and child: separate
realms, separate brands, free to diverge. The one thing that may not drift
between them is the `pf-*` class names, because the provider JAR's templates
(below) are shared by every realm and hardcode them.

## The email theme in particular

- `parent=base`, and base's own templates `<#import "template.ftl">` by name —
  which resolves through the theme chain. So the emails this theme does **not**
  override (org invites, identity-provider links, the `event-*` security
  notices) still render inside its shell. Only the messages this realm actually
  sends are overridden, to give them a heading and a real call-to-action button.
- All styling is inline; there is no stylesheet to serve and none of these files
  is reachable over HTTP.
- Copy lives in `email/messages/messages_en.properties` as **plain text**, and
  the same keys feed the HTML part and the `text/plain` alternative.

To look at one for real: the admin console's SMTP **Test connection** button
sends `email-test.ftl` through this theme, and is the only email you can trigger
on demand.

## The From display name

`fromDisplayName` lives inside the realm's `smtpServer` block, which the deploy
reconcile deliberately never pushes (it holds `${SMTP_*}` placeholders that only
the import substitutes). An established realm therefore keeps the From name it
was created with — change it in the admin console under **Realm settings →
Email** if it does not read `Protofast` (or `ThePlot`, on that realm).
