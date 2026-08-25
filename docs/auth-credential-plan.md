# Auth Credential Plan

**No passwords, in any realm.** An account is reached by **passkey**, by **Google / Apple**,
or by a **typed email code**. The passkey is offered at sign-in until it exists.

Companion to [`auth-architecture.md`](./auth-architecture.md) and
[`auth-implementation-guide.md`](./auth-implementation-guide.md). Supersedes that guide's §0
"Sign-in factors" row.

---

## 1. The model

| | all realms — `protofast` and tenants alike |
| --- | --- |
| Registration form | Email only (+ profile fields) |
| Email verification | Required, by typed code |
| Password | None. Not offered, not settable, not a required action |
| Passkey | Offered at sign-in, skippable, never a required action |
| Federated | Google, Apple (§2.4) |
| Fallback / recovery | Email code — a first-class login method |
| Admin console | Passkey via ACR step-up (§2.7) |

Keycloak 26.7 already drops the password field when self-registration and Verify Email are
both on, so this goes *with* the upstream default rather than against it.

### 1.1 The credential ladder

| User has | Sign-in path |
| --- | --- |
| Passkey | Passkey prompt (conditional UI / autofill on the email form) |
| Google / Apple link | IdP redirect |
| Neither — or a passkey not present on this device | Email code |

The email code is an **always-available alternative**, not a conditional branch: a passkey
holder on a new laptop has no passkey *there*, and gating the code behind "has no credential"
locks them out.

> **The consequence, load-bearing.** Today's flow gates the emailed-link branch on
> `CONDITIONAL`, so a mailbox alone can only open an account with no credential. That
> guarantee is gone by construction — the mailbox now opens every account, including one with
> a passkey. Two things follow: OTP brute-force handling becomes the most important control we
> ship (§2.2), and the admin surface must stop being reachable by mailbox alone (§2.7).

Google and Apple carry their own MFA and recovery; a bare mailbox carries whatever the user's
provider gives it. Federated is an upgrade on the email code; the passkey is an upgrade on
both.

### 1.2 Deliberately not doing

| Rejected | Why |
| --- | --- |
| Keeping an optional password | A credential we still have to police (policy, breach lists, reset flow, its own brute-force surface) that no flow depends on. |
| A third-party email-OTP extension | Its brute-force handling needs auditing regardless, and a small third-party repo can block a Keycloak security bump. |
| Passkey as a required action | No skip button — a device that cannot do WebAuthn is wedged with no exit. |
| SMS fallback | Second channel, per-message cost, SIM-swap surface, and the mailbox is still a reset path. |

---

## 2. Work items

### 2.1 Realm config

`reconcile_keycloak_realm` in [`deploy.sh`](../deploy/deploy.sh) pushes realm keys,
attributes, and required actions to live realms. Flow-level items are the exception (§2.3).

1. **`UPDATE_PASSWORD` → `enabled: false`** (today `enabled: true, defaultAction: false`).
   This is what must stop 26.7 attaching it to new registrations — **verify first, V2**.
2. **`passwordPolicy: ""`** and **`resetPasswordAllowed: false`**. Both already in
   `KC_REALM_KEYS` ([`deploy.sh:374`](../deploy/deploy.sh:374)); also drops the
   "Forgot password?" link.
3. **Disable direct access grants** on `protofast-web` and `admin` — ROPC has nothing left to
   grant.
4. **`webauthn-register-passwordless` stays `defaultAction: false`.** Record why in the realm
   README so nobody promotes it to a required action.
5. **Enable the 26.3 Passkeys switch** (WebAuthn Passwordless Policy) for conditional-UI
   autofill on the email form — it is what keeps the passkey path ahead of the code path in
   daily use. *Confirm the realm key, V5.*
6. **Add `webAuthnPolicyPasswordless*` to `KC_REALM_KEYS`.** None of it reconciles today, so
   it only ever applies to a realm created from scratch.
7. **Per-realm RP IDs.** `webAuthnPolicyPasswordlessRpId` is `protofast.dev`, which correctly
   covers `protofast.dev` + `admin.protofast.dev`. The browser will also offer that passkey on
   `myfitness.protofast.dev` / `theplot.protofast.dev` — different realms, so Keycloak rejects
   it, but its appearance in the picker leaks that the user has a ProtoFast account. Give each
   tenant realm its own RP ID before launch.
8. **Tenant-realm delta** is now just RP ID, IdP credentials, theme. Document it; do not fork
   the realm file.

### 2.2 Email OTP — new provider, built in-repo

Keycloak has no built-in email OTP, and this is the floor credential of every account.
One shared component, two entry points:

- **(a) `VERIFY_EMAIL` by code — a `RequiredActionProvider`.** The emailed *link* strands
  sign-ups: registering on a laptop and opening mail on a phone verifies the address but mints
  no session there. A code is readable anywhere and typed where you started. It also makes
  link-prefetching scanners harmless — they consume single-use action tokens before the user
  clicks. Replaces `VERIFY_EMAIL` as the realm's default action.
- **(b) An `Authenticator` for the sign-in flow.** Today's branch uses
  `reset-credential-email`, whose link **leaves the browser flow** into `resetCredentialsFlow`
  and lands on a set-a-password form. An authenticator stays in the session: send → form →
  verify → continue.

**Where it lives.** `infra/keycloak/providers/email-otp/` (Maven), mirroring
`infra/keycloak/themes` → `deploy/keycloak/themes`. Built JAR ships to
`deploy/keycloak/providers/`.

**Delivery.** Neither environment runs `--optimized` — dev is Aspire's `start-dev`, prod is
`kc.sh start --import-realm`
([docker-compose.host-b.yml:171](../deploy/docker-compose.host-b.yml:171)) — so the JAR should
bind-mount like the themes already do
([`Program.cs:47`](../apphost/Program.cs:47),
[docker-compose.host-b.yml:238](../deploy/docker-compose.host-b.yml:238)). **Verify first
(V1).** Fallback is a thin `FROM quay.io/keycloak/keycloak` image running `kc.sh build`,
costing a registry and a CI job.

**Build.** Maven inside `maven:3-eclipse-temurin-21` — no JDK on dev machines or CI images.
Pin `keycloak-server-spi` to `KEYCLOAK_TAG`; `kc.sh build` fails loudly on mismatch.

**Shape.** A shared code service (generate · send · verify · throttle), a
`RequiredActionProvider`, an `Authenticator`, factories, `META-INF/services`, and two
Freemarker templates shipped as `theme-resources/` inside the JAR so they inherit the
`protofast` theme's `pf-*` classes. Send via Keycloak's `EmailTemplateProvider` to reuse realm
SMTP and theme mail templates.

| Concern | Decision |
| --- | --- |
| Code | 6 digits, numeric |
| Lifetime | 10 minutes |
| Storage | Hashed in an authentication-session note, bound to the session id — not replayable elsewhere |
| Attempts | 5, then invalidate and force a resend |
| Resend | 60s cooldown, max 3 per session |
| Brute force | Register failures with Keycloak's `BruteForceProtector`. `failureFactor` is currently **30** — generous for a password, far too generous for a numeric code (§6.3) |
| Enumeration | Identical response and timing whether or not the address has an account |
| Mail failure | Surface a real error on the form, not a silent "check your inbox" |

### 2.3 Authentication flows — hand-run script

Flow changes stay out of `deploy.sh`: a bound flow half-rewritten by a best-effort deploy step
locks everyone out. Follow the `keycloak-apply-finish-setup-flow.py` pattern — idempotent,
`--dry-run`, run by hand and watched. (That script is superseded by
[`keycloak-apply-passwordless-flow.py`](../scripts/keycloak-apply-passwordless-flow.py), which
migrates a live realm onto the target below and then deletes the flow the old one built.)

Current:

```
passkey-or-password browser
  auth-cookie                        ALTERNATIVE
  identity-provider-redirector       ALTERNATIVE
  passkey-or-password forms          ALTERNATIVE
    auth-username-form               REQUIRED
    passkey-or-password credentials  REQUIRED
      passkey-or-password stored credential  CONDITIONAL
        conditional-user-configured          REQUIRED
        webauthn-authenticator-passwordless  ALTERNATIVE
        auth-password-form                   ALTERNATIVE
      finish account setup                   CONDITIONAL
        conditional-sub-flow-executed        REQUIRED  (credential-branch-skipped)
        reset-credential-email               REQUIRED
```

Target:

```
passwordless browser
  auth-cookie                        ALTERNATIVE
  identity-provider-redirector       ALTERNATIVE
  passwordless forms                 ALTERNATIVE
    auth-username-form               REQUIRED
    passwordless credentials         REQUIRED
      webauthn-authenticator-passwordless  ALTERNATIVE
      email-otp                            ALTERNATIVE
```

1. **Drop `auth-password-form`.** Existing password holders fall through to the email code
   with no migration step.
2. **Drop both CONDITIONAL sub-flows**, `conditional-user-configured`, and the
   `credential-branch-skipped` config. Keycloak skips the WebAuthn authenticator for a user
   with no passkey (`isConfiguredFor` → false) and offers **Try another way** to one who has
   it. **V3 must confirm both halves** — the second is the new-device lockout.
3. **Delete `finish account setup`** — its state is now the main path.
4. **Rename** `passkey-or-password *` → `passwordless *`; rebind `browserFlow`.
5. **Leave the built-in `reset credentials` flow alone**, unbound and unreachable behind
   `resetPasswordAllowed: false`.

### 2.4 Google & Apple

`identity-provider-redirector` is already `ALTERNATIVE` at the top of the browser flow, so
buttons render on the email form once providers exist.

**Google** is built-in. Client id/secret per realm into the existing secret plumbing
([`populate-secrets.sh`](../scripts/populate-secrets.sh)). Set `trustEmail: true`.

**Apple has no built-in provider.** Its client secret is a developer-signed ES256 JWT with a
**six-month maximum life**, so a static secret expires and takes Apple sign-in down.

- **Recommended:** a custom `apple` IdP in the JAR from §2.2 — subclass the OIDC provider and
  mint the secret per token request from the `.p8` key. Nothing to rotate.
- Alternative: generic OIDC plus a rotation job every ~5 months — less Java, one more thing to
  fail quietly.

Apple specifics: `response_mode=form_post`; name and email are returned **only on the first
authorization**; **Hide My Email** issues a per-app `@privaterelay.appleid.com` address, which
will not match an existing account and creates a second one (§5).

**Account linking.** Auto-link on verified email — both providers assert `email_verified`,
accounts are email-keyed via `registrationEmailAsUsername`, and `duplicateEmailsAllowed: false`
makes the match unambiguous. Auto-linking is only safe because *both* sides are verified;
write that next to the config.

### 2.5 auth-svc — post-auth routing and the passkey nag

Keycloak has no optional step inside a flow. The supported mechanism is an **Application
Initiated Action** — `kc_action` on a normal authorize request, with a cancel path, reported
via `kc_action_status`. Cancelling does not fail the login.

**The passkey is offered at sign-in and nowhere else** — no banner, no dashboard card, no
settings nudge. Sign-up gets one too, at the end of the registration round trip. A user who
cancels is asked again at the next sign-in, and **session lifetime is the cadence** (`ssoSessionIdleTimeout: 28800`,
`ssoSessionMaxLifespan: 604800` — roughly weekly for an active user). No backoff schedule, no
dismissal state.

#### Sign-up carries the action; sign-in chains it

`kc_action` goes on `/signup`'s own authorize request, alongside `prompt=create`, rather than
on a second round trip chained off the callback. Chaining works after a *sign-in* — the SSO
cookie Keycloak just set satisfies the follow-up authorize silently — but not after a
*registration*: registration is a different top-level flow and records no authenticated level
against the browser flow, so `CookieAuthenticator` answers the follow-up with
`Messages.authenticateStrong` and falls through to `passwordless forms`. In a realm whose only
non-passkey credential is a mailed code, that is a second code seconds after the one that just
verified the address. Verified against 26.7.2: registration + `kc_action` on one request mails
exactly one code and ends on the passkey page.

> **Watch this when billing lands.** The fork below sends an unsubscribed account to Angular,
> and §2.6.2 has the checkout end on an "add a passkey" screen. A sign-up has now already been
> offered one, so that screen must read `PasskeyRegisteredAt` (or the AIA's `skip_if_exists`)
> rather than nagging a second time. `/add-passkey` on a post-registration session hits the same
> Cookie-authenticator wall described above.

#### The callback fork

Every `/signin-oidc` callback ends with provision + Set-Cookie, then forks on **does this user
have a subscription?**

```
callback  (code → tokens → provision → Set-Cookie)
  │  ← if this leg carried the offer (always, for sign-up), record kc_action_status first
  │
  ├─ subscribed ──────→ offer not yet made? 302 KC authorize (kc_action=webauthn-register-passwordless)
  │                       └─ callback #2 (kc_action_status) → 302 returnUrl
  │                     already made? ─────→ 302 returnUrl
  │
  └─ not subscribed ──→ 302 returnUrl, flagged "subscribe"
                          └─ Angular checkout → success screen → "add a passkey"
                               └─ /add-passkey → AIA → 302 returnUrl
```

The unsubscribed branch hands off because the subscription workflow is Angular's (§2.6) and
takes minutes, a payment redirect, and a webhook. The subscribed branch never touches Angular
first — the app loads once, already past the nag. **Both branches end at the same doorway:**
the AIA is the only way a passkey gets registered, so Angular decides *when* to send the user
through `/add-passkey` and Keycloak performs the ceremony.

> **Today only the subscribed branch exists.** There is no subscription, so the callback chains
> into the AIA unconditionally. Build that; add the fork when billing lands.

#### Skip the round trip when a passkey exists

`skip_if_exists` no-ops the AIA, but that is still two redirects to learn nothing on every
sign-in for the users who complied. Read `PasskeyRegisteredAt` off the row `ProvisionAsync`
just wrote and skip the authorize; keep `skip_if_exists` as the backstop for a stale flag.

Keeping the flag honest without an Admin API call:

- `kc_action_status=success` on the AIA callback stamps it.
- **The `amr` claim self-heals it** — a user who *authenticated* with a passkey demonstrably
  has one, covering credentials added through Keycloak's account console.

> **Do not query the Admin API for existing passkeys.** `KeycloakGateway` only does token
> exchange; adding `view-users` puts the first standing admin credential into auth-svc for
> something a nullable timestamp and a claim already answer.

#### Work

1. [`KeycloakGateway.BuildAuthorizeUrl`](../services/auth/src/ProtoFast.Auth.Api/Keycloak/KeycloakGateway.cs:19) —
   optional `kcAction`, emitted like the existing `("prompt", registration ? "create" : null)`.
2. [`AuthFlow.StartAsync`](../services/auth/src/ProtoFast.Auth.Api/Endpoints/AuthFlow.cs) — pass
   `kcAction` through.
3. **New `/add-passkey` endpoint** in
   [`AuthEndpoints`](../services/auth/src/ProtoFast.Auth.Api/Endpoints/AuthEndpoints.cs) →
   `StartAsync` with the action set and **`skipIfAuthenticated: false`** (the default
   short-circuits on the live session and never reaches Keycloak — the same reason `/reset`
   opts out).
4. **`CorrelationData`** — a flag marking "this authorize was the passkey offer", so the
   callback tells the round-trips apart and never chains a third.
5. **`CallbackAsync`** — the fork: read `kc_action_status` on the second leg; on the first,
   either chain the AIA or redirect to `returnUrl` with the subscribe flag.
6. **`UserAccount`** — add `PasskeyRegisteredAt` plus an EF migration, and the subscription
   state the fork reads (§6.4).

### 2.6 Angular

1. **Subscribe indicator → checkout.** The callback redirects to `returnUrl` carrying the flag;
   the app routes into the subscription workflow instead of the dashboard.
2. **The workflow ends on an "add a passkey" screen**, not an automatic redirect. Its button is
   a full-page navigation to `/add-passkey?returnUrl=…` — a BFF endpoint, not an Angular route,
   same convention as the `/signin` links in
   [`landing.html`](../clients/protofast/src/app/pages/landing/landing.html).
3. **No persistent banner anywhere.**
4. **Beware the webhook race.** The payment provider returns the browser before the webhook
   marks the account subscribed; the success screen must tolerate "paid, not yet confirmed"
   rather than bouncing back into checkout.
5. **Route allowlist.** Whatever gates on the subscribe flag must exempt the subscription routes
   and `/signout`, or the redirect loops.

### 2.7 Admin console hardening

Flow A has staff sign in on `protofast.dev` with the admin console authenticating **silently**
off the shared realm SSO session ([`auth-architecture.md:103`](./auth-architecture.md:103)).
Without a password that means the admin console is reachable with a mailbox and nothing else,
for up to 7 days.

1. **Shorter client sessions on `admin`** — client-level idle/max overrides well below the
   realm's. Cheap, no flow work.
2. **`max_age` on the admin client's authorize request** in `BuildAuthorizeUrl` — kills silent
   SSO for admin only. On its own it forces *a* re-auth, which can still be an email code.
3. **ACR step-up** via `conditional-level-of-authentication`: map an ACR value to a level in the
   realm's `acr.loa.map`, add a CONDITIONAL sub-flow holding only
   `webauthn-authenticator-passwordless`, and have `BuildAuthorizeUrl` request that level for
   the `admin` client. The condition's **max age** is what makes it re-prompt rather than gating
   once per session. auth-svc must **verify the returned `acr` claim** — `acr_values` is
   voluntary, so asking is not getting. Gate rollout on `PasskeyRegisteredAt` coverage and a
   documented break-glass (§5).

> **Step-up narrows §1.1 rather than closing it.** An attacker holding the mailbox signs in at
> the low level, hits the admin gate with no passkey, and can **enrol one on the spot** through
> the same AIA. Closing that needs one of: additional-passkey enrolment requiring an existing
> one for privileged accounts; granting admin roles only after a passkey exists; or accepting
> the mailbox as root of trust and hardening *it* — staff mail on a provider with enforced
> hardware-key 2SV. The last is unglamorous and probably right for a team this size.

### 2.8 Theme & copy

1. **Style the OTP forms** (verify-email and sign-in), shipped as JAR theme resources — `pf-*`
   mapping and message keys.
2. **Exercise the WebAuthn pages.** `webauthn-register.ftl`, the passkey login pages, and the
   **Try another way** credential chooser all inherit from `base`;
   [`theme.properties`](../deploy/keycloak/themes/protofast/login/theme.properties) only maps
   classes for pages we have hit. §2.3.2 makes the chooser a real user path.
3. **Social buttons** — `kcFormSocialAccountList*` is unstyled; follow Google's and Apple's
   brand guidelines.
4. **Rewrite [`messages_en.properties`](../deploy/keycloak/themes/protofast/login/messages/messages_en.properties).**
   `updatePasswordMessage` describes a switch we no longer use; `emailExistsMessage` should
   point at sign-in, which now works for every account.
5. Make the "already registered" error a **link** to `/signin` — `register.ftl` renders it
   through `kcSanitize(...)?no_esc`, which permits a limited HTML subset.

### 2.9 Docs to update

- [`deploy/keycloak/realms/README.md`](../deploy/keycloak/realms/README.md) — its rationale
  ("`registration-password-action` defers the password") is moot; replace with §1's table and
  the "why `webauthn-register-passwordless` is not a required action" note.
- [`auth-architecture.md`](./auth-architecture.md) — Flow B's sign-up sequence, and Flow A's
  silent SSO versus §2.7.
- [`auth-implementation-guide.md`](./auth-implementation-guide.md) §0 — sign-in factors.

---

## 3. Settle on the dev container first

The Aspire dev stack already runs Keycloak 26.7.

**V1.** **Does a bind-mounted provider JAR survive `kc.sh start` without `--optimized`?** Biggest
   cost swing in the plan, and on the critical path. Test with a no-op provider.
**V2.** **Does `UPDATE_PASSWORD: enabled=false` stop 26.7 attaching it at registration?** If not,
   the fallback is a copied registration flow with the password action removed.
**V3.** **Does the flattened credentials sub-flow behave?** (a) no passkey → skips WebAuthn cleanly,
   lands on the code form; (b) has a passkey, on a device without it → can reach **Try another
   way → Email code**. (b) is a lockout if it fails; fallback is a theme-level link into the
   credential chooser.
**V4.** **Exact `skip_if_exists` syntax on `kc_action`**, and the `kc_action_status` value set
   (expected `success` / `cancelled` / `error`).
**V5.** **Realm key for the 26.3 "Enable Passkeys" switch**, for §2.1.5 and `KC_REALM_KEYS`.
**V6.** **Is `BruteForceProtector` callable from a custom authenticator and required action**, and do
   failures feed the same counters as password failures?
**V7.** **Apple**: confirm the generic OIDC provider's limits (form_post, secret lifetime) before
   committing to §2.4's recommendation.

### Answers, from Keycloak 26.7 (26.7.2 in the image)

**V1. Yes.** A bind-mounted JAR is picked up by a server started without
`--optimized`, in both environments. Registration shows as three
`KC-SERVICES0047 … internal SPI` warnings at boot; their absence means the mount
is wrong. No custom image, no registry, no CI job.

**V2. No — `enabled: false` is not enough on its own.** A disabled required action
is never *executed* (`AuthenticationManager` skips a model whose `isEnabled()` is
false), but `RegistrationPassword.success()` still calls
`user.addRequiredAction(UPDATE_PASSWORD)` unconditionally, so every new account
would carry a dead action that can never run. The fallback in §2.1.1 is what
shipped: a realm-owned registration flow with `registration-password-action`
**absent**. `enabled: false` stays as well, for accounts that already have one.

**V3(a). Yes.** A user with no passkey lands straight on the code form; Keycloak
skips the WebAuthn authenticator without rendering anything. Verified end to end
against a live realm: authorize → email form → mailed code → authorization code.
**V3(b) is untested** — it needs a real authenticator, which no headless check
can supply. What the code guarantees is that `email-otp` reports itself
`configuredFor` every user, which is the condition for it to appear in the
chooser; the chooser page and its `kcSelectAuthList*` classes are styled.

**V4.** `kc_action=<provider-id>` plus a **separate** `kc_action_parameter=skip_if_exists`
query parameter — not a suffix on the action. `kc_action_status` comes back
lower-cased, one of `success` / `cancelled` / `error`.

**V5.** `webAuthnPolicyPasswordlessPasskeysEnabled` (boolean), with
`webAuthnPolicyPasswordlessMediation: "conditional"` alongside it for autofill.
`Profile.Feature.PASSKEYS` is `Type.DEFAULT` in 26.7, so no `KC_FEATURES` entry is
needed. Note also that `webAuthnPolicyPasswordlessRequireResidentKey` is
deprecated in favour of `…ResidentKey` and logs on every read.

**V6. Yes, and it answers §6.3 too.** `BruteForceProtector` is reachable from both
a custom authenticator and a required action. But `failedLogin` takes an
`authenticationCategory` set, and `DefaultBruteForceProtector` **silently drops
anything outside `{password, otp, recovery-authn-codes}`** — a bespoke category
would mean wrong codes cost nothing at all. Reporting as `otp` puts OTP failures
in the same counters password failures used, so the OTP does **not** get a
separate realm-level counter: its own counter is the per-session cap in the
provider (5 attempts per code, 3 codes, 60s cooldown), and `failureFactor` — now
dropped to 10 — governs the realm-wide lockout.

**V7.** Not resolved by testing; the recommendation in §2.4 was taken on its
merits and the custom provider is built. It compiles against the 26.7 SPI and
registers cleanly. The Apple round trip itself needs real Apple credentials.

### One thing not on the list, found while building

**A CONDITIONAL sub-flow cannot sit beside an ALTERNATIVE at the same level.**
`DefaultAuthenticationFlow.fillListsOfExecutions` clears the entire alternative
list when anything REQUIRED or CONDITIONAL shares its level, logging one
`REQUIRED and ALTERNATIVE elements at same level!` warning. The obvious shape for
§2.7.3 — a step-up branch next to `auth-cookie` — therefore breaks cookie SSO and
the identity-provider redirect outright. Both conditional branches live inside
`passwordless forms` instead, and the ordinary sign-in is itself wrapped in a
level-1 condition, because an authentication that has not yet reached any level
matches *every* level condition. §2.3's target diagram is otherwise unchanged.

Second, smaller: Keycloak caps flow and execution **descriptions at 255
characters**, and a longer one fails the whole realm import with a bare
`{"errorMessage":"Database operation failed"}`.

---

## 4. Sequencing

| Order | Work | Gate |
| --- | --- | --- |
| 1 | V1 — no-op provider JAR, bind-mounted, both environments | none; prices everything downstream |
| 2 | V2 + §2.1.1–3 — kill `UPDATE_PASSWORD`, password policy, reset | none |
| 3 | §2.2(a) — `VERIFY_EMAIL` by code | V1, V6 |
| 4 | §2.2(b) + §2.3 — OTP authenticator, flow flattened, password form dropped | V1, V3, V6. **Passwordless sign-in is live here** |
| 5 | §2.1.4–8 realm config, §2.8 theme & copy | independently shippable |
| 6 | §2.5 + §2.6 — passkey nag, chained straight off the callback | parallel from step 3; the subscription fork lands with billing |
| 7 | §2.7.1–2 — admin session limits, `max_age` | independent |
| 8 | §2.4 — Google, then Apple | after step 4 |
| 9 | §2.7.3 — ACR step-up | staff passkey coverage + break-glass |
| 10 | §2.9 docs | with whichever change lands last |

There is **no one-switch win**. Step 4 is the change and it depends on shipping a Java provider,
which is why step 1 comes first and is cheap to abort. Until step 4, today's flow keeps working.

---

## 5. Risks

**The mailbox is the account.** Email OTP now opens accounts that have a passkey. Mitigations
are all downstream: OTP brute-force handling (§2.2), the sign-in nag (§2.5), admin step-up
(§2.7). If §2.7.3 never ships, the admin console is weaker than it is today.

**SMTP moves onto the sign-in path.** Every sign-in without a passkey or IdP link is a mail
delivery, so provider outages, greylisting, spam foldering, and latency become login failures.
Track SES bounce/complaint rates and delivery time as auth metrics.

**No offline break-glass in `protofast`.** SMTP down + no passkey = staff cannot sign in. The
escape hatch is Keycloak's **master realm** console, untouched here and still password-based —
confirm it is reachable in prod, credentialed somewhere real, and MFA'd, and write the procedure
down before §2.7.3 makes admin passkey-only.

**Java enters the repo permanently.** Maven build, pinned SPI version, and a Keycloak upgrade
that can now fail on our own code — which in §2.4's recommended design also owns a social login.

**Apple's Hide My Email creates duplicate accounts.** A relay address genuinely is a different
address. Detectable (`@privaterelay.appleid.com`); worth a copy warning, merge is out of scope.

**Migration is a no-op.** Existing password hashes stay in the database and simply stop being
reachable when `auth-password-form` leaves the flow. Deleting them is a later tidy-up, once
reverting is off the table.

---

## 6. Open decisions

1. **Converge registration and sign-in?** Both are now email → code. Staying separate is less
   flow surgery but keeps a wart: an unknown email on the sign-in form gets either an honest
   "no account" (enumeration) or a "check your inbox" for mail that never arrives. Converging —
   one form, unknown email creates the account on verification — closes enumeration and deletes
   the register form, at the cost of an authenticator that can create users and an
   `UPDATE_PROFILE` step for names. **Recommend: ship separate, converge after step 4.**
2. **Ever cut off the email code for passkey holders?** Restores the §1.1 guarantee at the cost
   of "lost your passkey → talk to a human". Reasonable for staff, hostile for product users;
   §2.7.3 sidesteps it for the surface that matters.
3. **`failureFactor` for OTP** — reuse the realm's 30, or give the OTP its own counter? §2.2
   assumes the latter; V6 answers it.
4. **Where does the subscription fact live?** §2.5's fork reads it at every callback, so it must
   be there without an extra hop.
   - **A Keycloak token claim** means a user attribute plus a protocol mapper, and a writer with
     `manage-users` on the Admin API — the standing admin credential §2.5 avoids — and it is
     stale between token refreshes.
   - **A claim auth-svc mints itself**, read from `UserAccount` at the callback it already
     performs and carried in the internal JWT it already issues
     ([`AuthorizationService`](../services/auth/src/ProtoFast.Auth.Api/Services/AuthorizationService.cs:11)
     sets `AuthHeaders.InternalJwt` today), with the billing webhook writing the row. No Admin
     API, no mapper, never stale.

   **Recommend the second** — Keycloak has no opinion about subscriptions, and `UserAccount`
   already documents itself as ProtoFast's local mirror of state living elsewhere.
