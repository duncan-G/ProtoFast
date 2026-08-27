# Account management

What a signed-in user can do to their own account without asking an operator: change the email
address that reaches it, add or remove a passkey, and delete the account outright. One page in each
Angular client (`/app/account`), six endpoints on auth-svc (`/account/*`), one narrowly scoped
Keycloak service account, and an SMTP relay.

## Where each thing actually happens

| Action | Who does it | Why there |
| --- | --- | --- |
| Change email | auth-svc, end to end | The address is the username and it gates the emailed sign-in code, so the new one has to be proved before it is written — but nothing about proving it needs Keycloak's UI. auth-svc mails a code and writes the address over the Admin API once it comes back. |
| Add a passkey | Keycloak, via the existing `/add-passkey` Application Initiated Action | Enrolment is a WebAuthn ceremony that needs a user gesture against Keycloak's own origin. It is the only route by which a passkey is ever enrolled, and the only thing here that leaves the app. |
| Remove a passkey | auth-svc, via the Admin API | There is no OIDC flow for "delete this credential". |
| Delete the account | auth-svc, via the Admin API | Same, plus the local data has to go in the same act. |

**Keycloak's account console is never linked to.** It is not part of this product's user
experience, and the deployment is expected to keep `/realms/*/account/*` unreachable from the
internet. Everything the console would have done for us is an ordinary API call, so the app makes
it. Passkey enrolment is the single exception, and it is a full-page navigation to a BFF endpoint,
never a router link: Angular decides *when* to send the user, Keycloak runs the ceremony.

## Endpoints (auth-svc, `Endpoints/AccountEndpoints.cs`)

| Endpoint | Behaviour |
| --- | --- |
| `GET /account/me` | The account as the page renders it: email, its WebAuthn credentials, and any email change still waiting on its code. `passkeysUnavailable` reports "Keycloak could not be asked" rather than failing the whole request. |
| `POST /account/email` | `{ newEmail }` → mails a six-digit code, or returns the live attempt if that address already has one inside the send cooldown. Nothing is written to Keycloak. |
| `POST /account/email/confirm` | `{ code }` → writes the parked address to Keycloak and commits the change everywhere else. |
| `DELETE /account/email` | Drops a parked change (wrong-code exhaustion). Closing the form does not call this — the mailed code stays enterable. |
| `DELETE /account/passkeys/{credentialId}` | Removes one credential. A credential Keycloak no longer has counts as success. |
| `POST /account/delete` | Deletes the Keycloak user, the local `UserAccount` row, and every session the browser holds. |

They run with **ext_authz OFF**, like the sign-in endpoints. Envoy sends the whole
`/account/` prefix to auth-svc (`proxy/envoy.vhost.yaml.tmpl`) so a new endpoint under that
prefix does not need its own regex — stuffing them into the OIDC allowlist once blew RE2's
program-size cap and left both apps with no routes. Because nothing
upstream vouches for the caller, every endpoint resolves the session cookie itself and never reads
the `x-user-id` family: on this route those headers are whatever the client sent. The two writes
also refuse a request whose `Origin` is another site, which is belt to the `SameSite=Lax` braces.

## Changing the email address

Two steps, both on our own origin, and the account does not move between them.

1. **`POST /account/email`** normalises the address, refuses one that is already on the account,
   refuses one another account already holds (`409 email_taken`), and mails a six-digit code to it. The pending change — the address, a salted SHA-256 of the
   code, and a 15-minute deadline — goes to Redis under `emailchg:{realm}:{sub}`, one per account,
   so asking again simply replaces it. Mail is paced separately from the parked change: sixty
   seconds before the same mailbox is written to again, and five mails per account per fifteen
   minutes. Closing the form does not delete the parked code — the mail already in the inbox
   still works, and asking for that same address again during the wait returns the live
   attempt instead of refusing. A *different* address after a typo is a different mailbox and
   can go out at once, unless the account has already spent its window.
2. **`POST /account/email/confirm`** checks the code — five wrong guesses and the change is
   dropped, because six digits is a small space — then does a read-modify-write of the Keycloak
   user, setting `email`, `username` and `emailVerified: true`. The address written is the one
   held in Redis, never one the request restates: the code proves *that* mailbox and no other.

**The address is not written until the code comes back.** It is the username and the only
destination an emailed sign-in code has, so committing a typo first — and asking Keycloak to send
its own verification mail afterwards — would turn a slip of the finger into a permanent lockout of
an account with no password to fall back on.

**A taken address is refused at both steps.** Step 1 asks Keycloak (`GET /users?email=…&exact=true`,
and the same query on `username`, since the write conflicts on either) before any mail goes out, so
a user who mistypes someone else's address learns it while they can still fix it rather than after
fetching a code that was never going to commit. Nothing reserves the address in between, so step 2
checks again and Keycloak's own uniqueness constraint — the one that cannot be raced — has the last
word: an address claimed inside those fifteen minutes still ends in `409 email_taken` at confirm
time. The step-1 check runs *before* the cooldown is taken, so a typo does not cost a minute; the
price is that the endpoint answers "does this address have an account here" for anyone holding a
session, which the confirm-time conflict conceded eventually in any case.

If Keycloak cannot be reached for that check, step 1 answers `503` rather than mailing a code for a
change that could not be written either.

Once Keycloak accepts, nothing else may fail the request: the local `UserAccount.Email`, this
browser's session record, and a heads-up to the *previous* address all follow, and each is logged
rather than surfaced. A session on the sibling host keeps the old address until its next refresh
re-reads the token — the same lag every other Keycloak-owned fact on that record has.

### auth-svc sends its own mail

The code and the change notification come from auth-svc, not Keycloak, so the service needs an
SMTP relay: `Auth_Smtp__*`, from the same `Auth_Smtp__*` Secrets Manager entries `deploy.sh`
already resolves into `.env` for Keycloak's own `SMTP_*`. One relay and one verified sender, with
two services on it.

An unconfigured relay is not a startup failure — sign-in never needs it — but `POST /account/email`
answers `503 mail_unavailable`, which the page renders as "we can't send confirmation codes right
now".

## Deletion is total, today

`POST /account/delete` erases the Keycloak user (taking its credentials and SSO sessions with it),
the local `UserAccount` row, and the BFF sessions — immediately, with no grace period and no
export. Keycloak goes first because it is the identity of record: if it refuses, nothing else has
happened and the user can try again, whereas the reverse order would leave an account that still
signs in and is simply re-provisioned on the next callback.

**When user-owned data lands in other services, deleting it belongs in `AccountFlow`, ahead of the
identity.** That method is the only thing in the system that knows an account is going away.

The browser is signed out by the same call — the endpoint clears the session cookie in its response
— so the page does not run `/signout` afterwards: there is no session left for Keycloak's
end-session to end. It shows the farewell and replaces itself with `/`, a full-page navigation that
drops the client's in-memory state along with the session, and `replace` so Back cannot return to an
account page with no account behind it.

## The one standing admin credential

Removing a credential and deleting a user are Admin API calls, so auth-svc holds a Keycloak service
account: the confidential client **`account-admin`**, whose service-account user has `view-users`
and `manage-users` on `realm-management` and nothing else. `manage-users` is realm-wide — this is
the credential to be careful with when hardening; the code narrows it to three calls a user makes
about their own account, all keyed by the subject in their own session.

The client runs with full scope on, which looks like the opposite of narrow and is not: a client
with full scope *off* strips every role it has no scope mapping for, so its token would carry
neither role and every Admin API call would 403. The client holds nothing but those two roles.

No sign-in path touches it. Whether an account has a passkey is still answered by
`UserAccount.PasskeyRegisteredAt` and the `amr` claim (credential plan §2.5), which is what keeps
the hot path free of Admin API round trips.

The secret travels as `Auth_Keycloak__AdminClientSecret` (Secrets Manager → `.env` as
`ACCOUNT_ADMIN_CLIENT_SECRET` → both the realm import and auth-svc). An empty secret does not fail
startup: sign-in is unaffected and the account endpoints answer 503, which the page renders as
"your passkeys couldn't be loaded just now".

## Applying this to a realm that already exists

The realm import only ever creates realms, and `deploy.sh`'s reconcile deliberately never creates
clients. Two changes therefore need a hand on an existing deployment:

1. **The `account-admin` client** — run
   [`scripts/keycloak-apply-account-admin-client.py`](../scripts/keycloak-apply-account-admin-client.py)
   once (it takes `--dry-run`). Idempotent; it creates the client, sets its secret from
   `ACCOUNT_ADMIN_CLIENT_SECRET`, and grants the two roles.
2. **`editUsernameAllowed: true`** — reconciled automatically (it is on `KC_REALM_KEYS`), so a
   deploy carries it. It was originally needed to make the account console's email field editable;
   that console is no longer used, and an `manage-users` admin can edit a username regardless. It
   is left on so the Admin API write of `username` alongside `email` is unambiguous across Keycloak
   versions — worth re-testing against a live realm before turning it off as a hardening step.
