# Keycloak realm import (prod)

This directory is the **prod** mount source for Keycloak's `--import-realm`
(`docker-compose.host-b.yml` mounts it at `/opt/keycloak/data/import`).

The canonical, hand-edited realm export lives in
[`infra/keycloak/realms/`](../../../infra/keycloak/realms/) (used by the dev
Aspire `WithRealmImport`). Keep this copy in sync with it — they are the same
committed realm config, staged here so the deploy bundle that syncs `deploy/` to
the host carries the realm without depending on `infra/`.

## The credential model

**No passwords, in any realm.** An account is reached by passkey, by Google or
Apple, or by a code typed from an email.

| | what the realm is configured to do |
| --- | --- |
| Registration form | Email only (+ profile fields) |
| Email verification | Required, by typed code (`verify-email-code`) |
| Password | None. `passwordPolicy` empty, `resetPasswordAllowed` false, `UPDATE_PASSWORD` disabled |
| Passkey | Offered at sign-in by the app, skippable, **never** a required action |
| Federated | Google and Apple, once their credentials are configured |
| Fallback | The emailed code — a first-class login method, not a branch |

The consequence is load-bearing and worth stating plainly: **the mailbox opens
every account**, including one that has a passkey. It has to, or a passkey holder
on a new laptop is locked out. Three things carry the weight that a password used
to: the code's own throttling (five attempts per code, three codes per session,
sixty-second cooldown), the realm's brute-force counters, and the admin console's
step-up branch below.

### Why `webauthn-register-passwordless` is not a required action

It is registered and enabled, but `defaultAction` is **false**, and it must stay
that way. A required action has no skip button, so a user on a device that cannot
do WebAuthn — an old browser, a locked-down work machine, a shared computer —
would be wedged at a prompt with no way past it and no way into their account.

Passkeys are offered instead through an Application Initiated Action: auth-svc
adds `kc_action=webauthn-register-passwordless` to an ordinary authorize request
right after a sign-in completes, and Keycloak reports back whether the user did
it or cancelled. Cancelling costs nothing and the offer comes round again at the
next sign-in.

### Why `failureFactor` is 10

It used to be 30, which is generous for a password and far too generous for a
six-digit code. With no password left in the realm, this number is now the
lockout threshold for guessing a code and nothing else.

## The browser flow

    passwordless browser                          (browserFlow)
      |- Cookie                                   ALTERNATIVE
      |- Identity Provider Redirector             ALTERNATIVE
      \- passwordless forms                       ALTERNATIVE
           |- passwordless sign-in                CONDITIONAL
           |    |- Condition - Level of Authentication  (level 1, max age 604800)
           |    |- Username Form                  REQUIRED
           |    \- passwordless credentials       REQUIRED
           |         |- WebAuthn Passwordless     ALTERNATIVE
           |         \- Email Code                ALTERNATIVE
           \- passwordless step-up                CONDITIONAL
                |- Condition - Level of Authentication  (level 2, max age 900)
                \- WebAuthn Passwordless          REQUIRED

Three parts of that shape are easy to "tidy" into a broken realm:

- **Nothing is ALTERNATIVE beside a CONDITIONAL.** Keycloak discards every
  ALTERNATIVE that shares a level with a REQUIRED or CONDITIONAL sibling, and
  logs a single `REQUIRED and ALTERNATIVE elements at same level!` warning while
  it does. Move the step-up branch up next to `Cookie` and cookie SSO and the
  identity-provider redirect both stop happening.
- **The level-1 condition is not decoration.** An authentication that has not yet
  reached *any* level matches every level condition, so without it the step-up
  branch fires on everybody's first sign-in and demands a passkey of them.
- **Both credentials are ALTERNATIVE and neither is conditional.** The email code
  reports itself available for every user on purpose. Gating it behind "has no
  other credential" would lock out exactly the people it exists for — and the
  **Try another way** chooser, which is how a passkey holder on a strange device
  reaches it, only appears because both options are live.

`acr.loa.map` maps `basic` → 1 and `passkey` → 2. Only the admin client asks for
`passkey`, through `acr_values` on its authorize request; auth-svc then verifies
the `acr` claim that comes back, because asking is not getting.

## The registration flow

    passwordless registration                     (registrationFlow)
      \- passwordless registration form
           \- Registration User Profile Creation

`registration-password-action` is **absent**, not disabled. Keycloak's
registration form action attaches an `UPDATE_PASSWORD` required action to every
new user whether or not the realm has that action enabled — and a disabled action
never runs, so it would sit pending on every account forever.

## The provider JAR

`email-otp` and `verify-email-code` are not built into Keycloak. They come from
[`../providers/protofast-keycloak.jar`](../providers/), built from
[`infra/keycloak/providers/email-otp`](../../../infra/keycloak/providers/email-otp/).
**The browser flow names `email-otp` by id, so a Keycloak that starts without
that JAR cannot run the flow at all.** The same JAR carries the `apple` identity
provider.

## Applying a change to a realm that already exists

`kc.sh start --import-realm` **skips a realm that already exists**, so editing
this file only affects a brand-new realm. For a running deployment:

- Realm-level flags, the allowlisted `attributes`, and required actions are
  pushed on every deploy by `reconcile_keycloak_realm` in `../../deploy.sh`. That
  now includes the whole `webAuthnPolicyPasswordless*` block — none of it
  reconciled before, so it only ever applied to a realm created from scratch,
  including the Passkeys switch that puts the passkey prompt on the email form.
- The allowlisted **client** settings are pushed too: the back-channel logout
  attributes and `frontchannelLogout`, plus the admin client's session
  idle/lifespan overrides. That is the only part of a client the reconcile
  touches — secrets, redirect URIs and flows stay owned by the import.
- A required action from a **new provider** is not registered automatically on an
  existing realm. Until it is, the reconcile can only warn that it does not
  exist.
- Authentication flows are not reconciled — a bound flow half-rewritten by a
  best-effort deploy step locks everyone out. Run
  [`scripts/keycloak-apply-passwordless-flow.py`](../../../scripts/keycloak-apply-passwordless-flow.py)
  once, by hand, watching it. It registers `verify-email-code`, builds both flows,
  rebinds the realm and then deletes the retired ones — in that order, so an
  interrupted run leaves a realm that still signs people in. It is idempotent and
  takes `--dry-run`.

## Adding a tenant realm

Do **not** fork this file. A tenant realm differs from `protofast` in exactly
three things:

1. **`webAuthnPolicyPasswordlessRpId`.** It is `protofast.dev` here, which
   correctly covers `protofast.dev` and `admin.protofast.dev`. A browser will
   also offer that passkey on `myfitness.protofast.dev`, and although Keycloak
   rejects it — different realm — its appearance in the picker leaks that the
   user has a ProtoFast account. Give every tenant realm its own RP ID before
   launch.
2. **Identity provider credentials.** Google and Apple are configured per realm.
3. **`loginTheme` / `emailTheme`**, if the tenant is branded.

Everything else — the flows, the required actions, the brute-force numbers, the
credential model — is the same, and should stay literally the same.

## Editing this file

Flow and execution **descriptions are capped at 255 characters** in Keycloak's
schema. A longer one fails the whole import with a bare
`{"errorMessage":"Database operation failed"}` and no indication of which field
is at fault. Long rationale goes here, not in the JSON.
