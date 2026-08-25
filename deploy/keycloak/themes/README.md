# Keycloak themes (prod)

This directory is the **prod** mount source for Keycloak's custom themes
(`docker-compose.host-b.yml` mounts it at `/opt/keycloak/themes`, read-only).

The canonical, hand-edited theme lives in
[`infra/keycloak/themes/`](../../../infra/keycloak/themes/) (mounted by the dev
Aspire host at `/opt/keycloak/themes`). Keep this copy in sync with it — they are
the same committed theme, staged here so the deploy bundle that syncs `deploy/`
to the host carries the theme without depending on `infra/`.

`protofast` ships two theme types, both selected in the realm import
(`../realms/protofast-realm.json`) and both reconciled onto an already-existing
realm by `KC_REALM_KEYS` in `../../deploy.sh`:

| type    | realm key    | what it covers |
| ------- | ------------ | -------------- |
| `login` | `loginTheme` | sign-in, sign-up, reset/update password, OTP, verify-email, error pages |
| `email` | `emailTheme` | every message the realm sends |

Both render the Nocturne design system (see
[`clients/protofast/src/styles/nocturne.css`](../../../clients/protofast/src/styles/nocturne.css)),
so mail and auth pages read as the same product.

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
Email** if it does not read `Protofast`.
