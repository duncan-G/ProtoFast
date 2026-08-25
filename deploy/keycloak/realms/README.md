# Keycloak realm import (prod)

This directory is the **prod** mount source for Keycloak's `--import-realm`
(`docker-compose.host-b.yml` mounts it at `/opt/keycloak/data/import`).

The canonical, hand-edited realm export lives in
[`infra/keycloak/realms/`](../../../infra/keycloak/realms/) (used by the dev
Aspire `WithRealmImport`, plan §2.2). Keep this copy in sync with it — they are
the same committed realm config (Q2), staged here so the deploy bundle that syncs
`deploy/` to the host carries the realm without depending on `infra/`.

## The browser flow's second branch

`passkey-or-password credentials` has two conditional branches, not two
authenticators:

    passkey-or-password credentials
      |- passkey-or-password stored credential   CONDITIONAL  (user has one)
      |    |- Condition - user configured
      |    |- WebAuthn Passwordless              ALTERNATIVE
      |    \- Password Form                      ALTERNATIVE
      \- finish account setup                    CONDITIONAL  (user has neither)
           |- Condition - sub-flow executed  ->  stored credential branch skipped
           \- Send Reset Email

Sign-up creates the account before any credential exists — `registration-password-action`
defers the password until after email verification — so an account abandoned at
the verify-email or set-password step has nothing to authenticate with. Offering
only the two credential authenticators meant those accounts got "Invalid username
or password" forever, and signing up again only got "Email already exists". The
second branch emails them a link to finish setting up instead; the required
actions still pending (VERIFY_EMAIL, then UPDATE_PASSWORD) run when they follow it.

`attributes` raises the action-token lifespans off Keycloak's 5-minute default
for the same reason: every link this flow sends is one somebody opens later.

## Applying a change to a realm that already exists

`kc.sh start --import-realm` **skips a realm that already exists**, so editing
this file only affects a brand-new realm. For a running deployment:

- Realm-level flags, the allowlisted `attributes`, and required actions are
  pushed on every deploy by `reconcile_keycloak_realm` in `../../deploy.sh`.
- The allowlisted **client** settings are pushed too: the back-channel logout
  attributes (`KC_CLIENT_ATTRS`) and `frontchannelLogout` (`KC_CLIENT_FIELDS`,
  a plain field rather than an entry in the `attributes` map). All three move
  together — Keycloak either redirects the browser through the client or POSTs
  it a logout token, so a logout URL reconciled onto a client still marked
  `frontchannelLogout: true` is configured and never called. That is the only
  part of a client the reconcile touches: secrets, redirect URIs and flows stay
  owned by the import. The logout URL is read from the running Keycloak
  container's `BACKCHANNEL_LOGOUT_URL`, because this file only ever holds the
  import placeholder.
- Authentication flows are not — a bound flow half-rewritten by a best-effort
  deploy step locks everyone out. Run
  [`scripts/keycloak-apply-finish-setup-flow.py`](../../../scripts/keycloak-apply-finish-setup-flow.py)
  once, by hand, watching it. It is idempotent and takes `--dry-run`.
