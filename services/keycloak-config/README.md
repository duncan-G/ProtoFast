# keycloak-config

keycloak's configure task: it makes the realms `protofast`, `theplot`, `offtheloop` and `operators` match
the files in `realms/` through Keycloak's admin API, with
[keycloak-config-cli](https://github.com/adorsys/keycloak-config-cli), and creates a realm
that is missing. ProtoStrap runs it once keycloak has a ready replica, and again when this
task's image, `env` or one of its secret keys changes, and when keycloak's own release
changes (ProtoStrap's [configure tasks](https://github.com/duncan-G/protostrap/blob/main/docs/config.md#configure-tasks)).
Under `aspire run` it runs once keycloak is healthy. Every run applies all three realms,
the same way twice; it exits 0 only when all of it is applied, and logs to stdout.

| Path                      | What it is                                                         |
| ------------------------- | ------------------------------------------------------------------ |
| `Dockerfile`              | Builds keycloak-config-cli 6.5.1 from source, adds the realms and the entrypoint |
| `entrypoint.sh`           | Turns ProtoStrap's and the AppHost's variables into keycloak-config-cli's, then runs it |
| `realms/*-realm.json`     | The realms, in Keycloak's export format, with `$(env:NAME)` substitutions |
| `test/entrypoint_test.sh` | The entrypoint's tests                                             |

Keycloak's own image, with the providers and themes these realms name, is
[`../keycloak`](../keycloak). The realms were imported by `kc.sh start --import-realm`
before; that only ever creates a realm, so a change to a file never reached one that
existed. This task applies every change, every time.

## What it reads

| Variable                            | From (in an account; locally)                                         | Use |
| ----------------------------------- | --------------------------------------------------------------------- | --- |
| `KEYCLOAK_URL`                      | The spec's `env`: `http://keycloak:8080`; Aspire's endpoint reference | Where the admin API is |
| `KC_BOOTSTRAP_ADMIN_CLIENT_ID`      | The spec's `env`: `protofast-admin`                                    | The login (client credentials) |
| `KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`  | The secret's `Keycloak_KC_BOOTSTRAP_ADMIN_CLIENT_SECRET`, a secret file | The login (client credentials) |
| `KEYCLOAK_GRANTTYPE`, `KEYCLOAK_USER`, `KEYCLOAK_PASSWORD` | Locally only: `password`, `admin` and the admin's password | The login (password grant) |
| `ORIGIN_PROTOFAST`, `ORIGIN_THEPLOT`, `ORIGIN_OFFTHELOOP`, `ORIGIN_ADMIN` | ProtoStrap and the AppHost: `https://protofast.dev`, `http://protofast.dev.localhost:20000` | Each web client's redirect URI (`<origin>/signin-oidc`), post-logout URIs (`<origin>/*`), web origins (`<origin>` and `+`) and home URL |
| `HOST_PROTOFAST`, `HOST_AUTH`       | ProtoStrap and the AppHost: `protofast.dev`, `auth.protofast.dev`      | The WebAuthn RP IDs, and the From address, `no-reply@<HOST_PROTOFAST>` |
| `AUTH_URL`                          | The spec's `env`: `http://auth:8080`; auth's endpoint as Keycloak's container reaches it | Back-channel logout, `<AUTH_URL>/backchannel-logout` |
| `Keycloak__ClientSecretProtofastWeb`, `Keycloak__ClientSecretTheplotWeb`, `Keycloak__ClientSecretOfftheloopWeb`, `Keycloak__ClientSecretAdmin` | The secret's `Shared_` keys, secret files; AppHost parameters | The `protofast-web`, `theplot-web`, `offtheloop-web` and `admin` clients' secrets |
| `Keycloak__AdminClientSecret`       | The secret's `Shared_Keycloak__AdminClientSecret`, a secret file       | Every realm's `account-admin` client's secret |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `APPLE_CLIENT_ID`, `APPLE_TEAM_ID`, `APPLE_KEY_ID`, `APPLE_PRIVATE_KEY` | Nothing passes them yet, so they are empty ([layer 05](../../docs/05-identity.md)) | The `google` and `apple` identity providers of `protofast`, `theplot` and `offtheloop`, which ship disabled |
| `SMTP_FROM`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_SSL`, `SMTP_STARTTLS`, `SMTP_AUTH`, `SMTP_USER`, `SMTP_PASSWORD` | Locally, the AppHost: smtp4dev's host and port | The realms' `smtpServer`; defaults `no-reply@<HOST_PROTOFAST>`, `localhost`, `1025`, `false`, `false`, `false`, empty, empty. SES ignores all but `from` |
| `WEBAUTHN_RP_ID`, `THEPLOT_WEBAUTHN_RP_ID`, `OFFTHELOOP_WEBAUTHN_RP_ID`, `OPERATORS_WEBAUTHN_RP_ID` | Worked out by the entrypoint, unless set                  | Each realm's passwordless WebAuthn RP ID |
| `DEV_OPERATORS`                     | Locally only, the AppHost                                             | The operators realm's dev users (below) |
| `PROTOSTRAP_REPLICA`                | ProtoStrap                                                            | JSON log lines (`SPRING_PROFILES_ACTIVE=json-log`), unless set |

The entrypoint fails, naming each one that is missing and never a value, without
`KEYCLOAK_URL`, the login's variables, the origins, `HOST_PROTOFAST`, `HOST_AUTH`,
`AUTH_URL` or one of the four client secrets. The client secrets have no default: a known
development value can't reach an account that forgot a key.

**Secret files.** Under ProtoStrap the task has `secret_files: true`, and the entrypoint
exports each regular file in `/run/secrets` as the variable of its name, as keycloak's
does: it follows the agent's `..data` links, skips `previous/` and names that aren't
variable names, and never replaces a variable that is already set. Locally the AppHost sets
the same names as variables.

**The login.** By default, and always in an account, keycloak-config-cli logs in to the
master realm with the client credentials grant of the bootstrap admin client that keycloak
creates on its first start from the same two variables. Locally, a database that already
had a master admin user when the bootstrap client came in never gets that client (Keycloak
creates the bootstrap admins only with a new master realm), so the AppHost gives this task
`KEYCLOAK_GRANTTYPE=password`, `KEYCLOAK_USER=admin` and `KEYCLOAK_PASSWORD`, and it logs in
with admin-cli's password grant instead. Any other grant type fails.

**WebAuthn RP IDs.** The RP ID must be the host of the page that runs the ceremony, which
is Keycloak's (`HOST_AUTH`), or a parent of it. `protofast`'s is `HOST_PROTOFAST` when that
is a parent of `HOST_AUTH` (`protofast.dev` for `auth.protofast.dev`), and `HOST_AUTH`
otherwise (locally, `auth.dev.localhost` isn't under `protofast.dev.localhost`). `theplot`'s,
`offtheloop`'s and `operators`' are always `HOST_AUTH`. Each realm has its own, so no tenant's picker
offers another realm's passkeys (see [Adding a tenant realm](#adding-a-tenant-realm)).

**Values in the realm files.** keycloak-config-cli substitutes `$(env:NAME)` and
`$(env:NAME:-default)` into the files' text before it parses them, so the entrypoint first
escapes each variable the files name for a JSON string: `\` and `"`, line breaks and other
control characters, and `$(`, so that a value is never taken for a variable. A multi-line
Apple key or a secret holding a quote then arrives as it is, and can't break a file or show
up in a parse error's message. All the files' substitutions are inside JSON strings, which
is what makes this right; keep them there.

## How it applies the realms

The `Dockerfile`'s comments give each setting's reason. In short:

- **Every run applies everything** (`IMPORT_CACHE_ENABLED=false`). keycloak-config-cli can
  skip a realm whose checksum, kept in the realm's attributes, hasn't changed. The checksum
  is of the substituted text, so a new secret or hostname would change it, but it is
  written only at the end of a successful run: the rollback after a run that failed halfway
  would apply the last good files, whose checksum is the one kept, and skip the half-changed
  realm. It would also skip ProtoStrap's run after a Keycloak upgrade, and never put back a
  change made in the admin console.
- **It deletes only what it applied** (`IMPORT_REMOTESTATE_ENABLED=true`). It keeps, in
  each realm's attributes (`de.adorsys.keycloak.config.state-default-*`), the names of the
  clients, roles, required actions and components it applied, and deletes one only when it
  applied it before and it has left the files. What Keycloak makes itself (the built-in
  clients, roles and flows, the realm keys, each client's service account user) and what an
  admin adds in the console stay.
- **Flows and identity providers belong to the files.** They have no such state: a
  top-level flow that isn't built in and isn't in the files is deleted (unless a realm
  binding or an identity provider uses it), and so is an identity provider that isn't in
  the files. Add one in the files, never in the console.
- **Users are never deleted**: keycloak-config-cli can't purge users. It creates or updates
  only the users the files list (each realm's `service-account-account-admin`, to give it
  `view-users` and `manage-users` on `realm-management`) and `DEV_OPERATORS`' users, and
  sets those users' roles to exactly the listed ones, apart from `default-roles-<realm>`.
  Everyone who signs up is untouched.
- **A run that changes nothing changes nothing.** It updates each realm's settings and
  each identity provider (Keycloak never returns a provider's client secret, so it can't
  tell it is unchanged), and compares the rest: a client, flow, required action, component
  or user that matches its file is left as it is, with the same id.

### The browser flow and keycloak-config-cli

keycloak-config-cli changes a flow by deleting it and creating it again. For the bound
browser flow it first binds an empty temporary flow (`TEMPORARY_CREATED_AUTH_FLOW`),
recreates `passwordless browser` with all its sub-flows (a change to any sub-flow, even a
description, recreates the whole top-level flow), binds it again and deletes the temporary
one. **While that runs, sign-in fails**: changing one sub-flow's description, about a
second of a login page polled every 50 ms answered HTTP 400 (15 of 685 requests), then the
flow was back. A run that fails in the middle can leave the temporary flow bound, so no one
can sign in until the next run, or the rollback's, binds the right one again. So change a
flow when a second of failed sign-ins doesn't matter, and watch the run. A run that doesn't
change a flow doesn't touch it: it compares each flow first (logging `No need to update
flow` at debug), and the flows keep their ids.

## Dev operators

Under `aspire run`, the AppHost sets `DEV_OPERATORS`:

```text
DEV_OPERATORS=protofast@admin.test=platform,admin-protofast;theplot@admin.test=admin-theplot
```

Each entry, separated by `;`, is an email, `=`, and realm roles separated by `,`. The
entrypoint writes them to a file of the `operators` realm's users,
`$TMPDIR/keycloak-config/dev-operators.json`, imported after the three realms: each user is
enabled, has a verified email and holds exactly those roles, with no password, since an
operator signs in with an emailed code (it arrives in smtp4dev, `http://localhost:8025`).
On the first run Keycloak gives a new user the realm's default required action,
`verify-email-code`, which passes at once for a verified address; the next run clears it.
A role that isn't in the realm fails the run; an entry it can't read fails it before
keycloak-config-cli starts, naming the entry. Users it made stay when `DEV_OPERATORS` goes.

The file names only `realm` and `users`, and keycloak-config-cli updates a realm with what
a file names, so it leaves the rest of the realm as `operators-realm.json` set it: checked
by comparing every realm setting, client, flow, required action, component and role, with
their ids, before and after.

Never in an account. How an Org Admin makes the first operator there is still to be decided:
`scripts/keycloak-grant-admin.py` needs Keycloak's admin API, which the edge doesn't route and
only the overlay reaches ([open items](../../docs/protostrap-migration.md#open-items)).

## The credential model

**No passwords, in any realm.** An account is reached by passkey, by Google or Apple, or by
a code typed from an email.

| | What the realm is configured to do |
| --- | --- |
| Registration form | Email only (+ profile fields) |
| Email verification | Required, by typed code (`verify-email-code`) |
| Password | None. `passwordPolicy` empty, `resetPasswordAllowed` false, `UPDATE_PASSWORD` disabled |
| Passkey | Offered at sign-in by the app, skippable, **never** a required action |
| Federated | Google and Apple, once their credentials are configured |
| Fallback | The emailed code: a first-class login method, not a branch |

The consequence is load-bearing and worth stating plainly: **the mailbox opens every
account**, including one that has a passkey. It has to, or a passkey holder on a new laptop
is locked out. Three things carry the weight that a password used to: the code's own
throttling (five attempts per code, three codes per session, sixty-second cooldown), the
realm's brute-force counters, and the admin console's step-up branch below.

### Why `webauthn-register-passwordless` is not a required action

It is registered and enabled, but `defaultAction` is **false**, and it must stay that way. A
required action has no skip button, so a user on a device that cannot do WebAuthn (an old
browser, a locked-down work machine, a shared computer) would be wedged at a prompt with no
way past it and no way into their account.

Passkeys are offered instead through an Application Initiated Action: auth adds
`kc_action=webauthn-register-passwordless` to an ordinary authorize request right after a
sign-in completes, and Keycloak reports back whether the user did it or cancelled.
Cancelling costs nothing and the offer comes round again at the next sign-in.

### Why `failureFactor` is 10

It used to be 30, which is generous for a password and far too generous for a six-digit
code. With no password left in the realm, this number is the lockout threshold for guessing
a code and nothing else.

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

- **Nothing is ALTERNATIVE beside a CONDITIONAL.** Keycloak discards every ALTERNATIVE that
  shares a level with a REQUIRED or CONDITIONAL sibling, and logs a single
  `REQUIRED and ALTERNATIVE elements at same level!` warning while it does. Move the step-up
  branch up next to `Cookie` and cookie SSO and the identity-provider redirect both stop
  happening.
- **The level-1 condition is not decoration.** An authentication that has not yet reached
  *any* level matches every level condition, so without it the step-up branch fires on
  everybody's first sign-in and demands a passkey of them.
- **Both credentials are ALTERNATIVE and neither is conditional.** The email code reports
  itself available for every user on purpose. Gating it behind "has no other credential"
  would lock out exactly the people it exists for, and the **Try another way** chooser,
  which is how a passkey holder on a strange device reaches it, only appears because both
  options are live.

`acr.loa.map` maps `basic` → 1 and `passkey` → 2. Only the admin client asks for `passkey`,
through `acr_values` on its authorize request; auth then verifies the `acr` claim that comes
back, because asking is not getting.

The passkey prompt on the email form comes from `webAuthnPolicyPasswordlessPasskeysEnabled`
with `webAuthnPolicyPasswordlessMediation: conditional`; the second is a Keycloak 26.7
field, which is why this image builds keycloak-config-cli with a newer client library
([Upgrading](#upgrading)).

## The registration flow

    passwordless registration                     (registrationFlow)
      \- passwordless registration form
           \- Registration User Creation with Claim

`registration-password-action` is **absent**, not disabled. Keycloak's registration form
action attaches an `UPDATE_PASSWORD` required action to every new user whether or not the
realm has that action enabled, and a disabled action never runs, so it would sit pending on
every account forever.

### Signing up again with an address you never verified

`protofast-registration-user-creation` replaces Keycloak's stock user creation
(`registration-user-creation`) and differs from it in one case. Sign-up writes the account
and *then* asks for a mailed code, so closing the tab at that screen leaves a **shell**: an
account holding an address nobody has ever proved. Stock Keycloak answers the next sign-up
with "email already registered" forever: the one thing you cannot do with that address is
the thing you were trying to do. Here that sign-up resumes the shell instead: same row, same
code screen, no duplicate.

Unverified means unproved *in this realm*: every way in ends at a mailed code or a passkey,
and the code path sets `emailVerified`. So a shell has no session, no local user row and
nothing to take. A stranger who guesses someone's abandoned address gets a mail they cannot
read and reaches nothing; the code is still the only way through. Anything that is not a
shell (verified, holds a credential, linked to Google or Apple, federated, disabled) is
refused exactly as before, and the form links to sign-in.

A shell stays reclaimable from sign-up until the address is proved. Mail is paced
separately: sixty seconds between codes to the same address (stamped on the account, so a
new tab does not reset it) and three codes per authentication session. Hitting either limit
stays on the code screen and leaves the last mailed code valid; it never turns the next
sign-up into "already registered".

## Account management

Two things in these files exist for the account page (`docs/05-identity.md`), not for
sign-in:

- **`account-admin`** is a confidential client with service accounts on and every browser
  flow off. Its service-account user holds `view-users` and `manage-users` on
  `realm-management` and nothing else: the whole of the standing admin credential in this
  system. auth uses it for the calls a user makes about their own account that no OIDC flow
  expresses: list my passkeys, remove one, delete my account, and write a new email address
  onto it. The sign-in path still never asks the admin API anything. Its `fullScopeAllowed`
  is `true` and has to be: a client with full scope off strips every role it has no scope
  mapping for, and the resulting token, carrying neither of the two roles, is one the admin
  API answers 403 to. The client holds nothing but those roles, so full scope here *is* that
  grant.
- **`editUsernameAllowed` is `true`.** With `registrationEmailAsUsername` the email *is* the
  username, so an email change is a username change, and auth writes both in one admin API
  call once it has verified the new address itself. A `manage-users` admin can edit a
  username whatever this flag says, so the flag is belt to that braces rather than the thing
  that permits the write; re-test it against a live realm before turning it off.

Both reach a realm that already exists: this task applies them like everything else.

## Adding a tenant realm

Do **not** fork a file blindly. A tenant realm differs from `protofast` in exactly three
things:

1. **`webAuthnPolicyPasswordlessRpId`.** Give every tenant realm its own RP ID before
   launch, or the tenant's picker offers ProtoFast passkeys that Keycloak then rejects
   (different realm), leaking that the user has a ProtoFast account. The RP ID must be the
   host of the page that runs the ceremony, or a parent of it, and that page is Keycloak's
   (`auth.protofast.dev`), not the tenant app's: `theplot.protofast.dev` makes the browser
   refuse every registration outright. `theplot` and `offtheloop` use `HOST_AUTH`, the one
   other valid value; the entrypoint sets it.
2. **Identity provider credentials.** Google and Apple are configured per realm.
3. **`loginTheme` / `emailTheme`**, if the tenant is branded.

Everything else (the flows, the required actions, the brute-force numbers, the credential
model) is the same, and should stay literally the same. A new realm file also needs its
client's secret as a new key, required by the entrypoint, and its origin.

## Editing the realm files

- **Flow and execution descriptions are capped at 255 characters** in Keycloak's schema. A
  longer one fails the whole import with a bare `{"errorMessage":"Database operation
  failed"}` and no indication of which field is at fault. Long rationale goes here, not in
  the JSON.
- **Substitutions stay inside JSON strings**, as `"$(env:NAME)"` or `"$(env:NAME:-default)"`
  (keycloak-config-cli's syntax; Keycloak's own `${...}` placeholders are no longer read),
  and a new variable without a default is added to the entrypoint's `require` list and its
  test. Keycloak's own `${...}` message keys, in a display name for instance, are left as
  they are: keycloak-config-cli's variables start with `$(`.
- **Never turn on `LOGGING_LEVEL_REALMCONFIG=trace`** outside a throwaway Keycloak: it logs
  the substituted files, secrets included.

## Upgrading

- **keycloak-config-cli** reads the files into the representations of Keycloak's client
  library (`keycloak-client-common-synced`), and refuses a field it doesn't know. Upstream's
  newest image, 6.5.1 built against Keycloak 26.5.5, has the client library 26.0.6, which
  doesn't know 26.7's `webAuthnPolicyPasswordlessMediation`, so the `Dockerfile` builds the
  same release from its source tarball (pinned by checksum) with the client library 26.0.12,
  and runs it on upstream's 6.5.1 image (a Java 21 runtime, linux/amd64 and linux/arm64).
  When a release whose image has a client library that knows every field in these files
  comes out, use its image as it is and drop the build stage. On a Keycloak upgrade, check
  that the client library knows any realm field the new version adds to these files.
- **Keycloak**: see [`../keycloak/README.md`](../keycloak/README.md#the-spi-must-match-the-server).

## Tests

```bash
# The entrypoint's, from services/keycloak-config (bash 4.4 or later):
bash test/entrypoint_test.sh
```

The end-to-end check is a scratch run: PostgreSQL 18, keycloak's image started with the
bootstrap admin client, then this image with the variables above, twice (the second run
changes nothing: every id stays the same), once with a client secret changed (the new one
is applied), once with secret files in `/run/secrets`, once with the password grant, and
once with `DEV_OPERATORS`.
