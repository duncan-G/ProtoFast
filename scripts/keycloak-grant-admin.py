#!/usr/bin/env python3
"""Grant or revoke admin console roles in the `operators` realm.

There is no operator sign-up: the realm has registration off, and this script is how an operator
comes to exist (docs/design/admin-consoles.md §4.5). Idempotently, it:

1. ensures each named role exists in the realm;
2. creates the user by email if missing, with a required action to register a passkey on first
   sign-in;
3. adds (or with --revoke, removes) the role mappings;
4. prints the subject and the user's effective admin-* / platform roles.

Roles: `--app APP` grants `admin-APP` (repeatable; APP must be a known app, so a typo cannot mint
`admin-teplot`), `--platform` grants `platform`.

Usage (dev, against the Aspire Keycloak; --insecure only if it serves the self-signed cert):

    KC_URL=http://localhost:8080 KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=... \\
        scripts/keycloak-grant-admin.py --app theplot someone@example.com

Usage (prod): mint the throwaway service account the way
scripts/keycloak-apply-account-admin-client.py documents, run with KC_ADMIN_CLIENT_ID /
KC_ADMIN_CLIENT_SECRET, then delete that client again so no standing admin credential remains.

A revoke takes effect at the operator's next access-token refresh: auth drops a session whose
account holds no console role, and every admin RPC re-checks the role.

Pass --dry-run to see what it would change. --no-passkey skips the passkey required action, for
dev accounts on a browser that cannot do WebAuthn.
"""

import argparse
import json
import os
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request

KC_URL = os.environ.get("KC_URL", "http://localhost:8080").rstrip("/")
REALM = os.environ.get("KC_REALM", "operators")

# Must match AdminAccess.KnownApps (services/shared/Grpc/AdminAccess.cs).
KNOWN_APPS = {"protofast", "theplot"}
PLATFORM_ROLE = "platform"
PASSKEY_ACTION = "webauthn-register-passwordless"

parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
parser.add_argument("email")
parser.add_argument("--app", action="append", default=[], help="grant admin-APP (repeatable)")
parser.add_argument("--platform", action="store_true", help="grant platform")
parser.add_argument("--revoke", action="store_true", help="remove the roles instead")
parser.add_argument("--no-passkey", action="store_true", help="do not require a passkey on first sign-in")
parser.add_argument("--dry-run", action="store_true")
parser.add_argument("--insecure", action="store_true", help="skip TLS verification (dev only)")
ARGS = parser.parse_args()

TLS = ssl._create_unverified_context() if ARGS.insecure else None


def say(message):
    print(("[dry-run] " if ARGS.dry_run else "") + message)


def urlopen(url, data=None):
    return urllib.request.urlopen(url, data, context=TLS)


class Admin:
    """Just enough of the Admin API, against one realm."""

    def __init__(self, token):
        self.token = token

    def call(self, method, path, body=None):
        url = f"{KC_URL}/admin/realms/{urllib.parse.quote(REALM)}/{path}"
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data, method=method)
        req.add_header("Authorization", f"Bearer {self.token}")
        if data is not None:
            req.add_header("Content-Type", "application/json")
        with urlopen(req) as response:
            raw = response.read()
        return json.loads(raw) if raw else None


def requested_roles():
    unknown = sorted(set(ARGS.app) - KNOWN_APPS)
    if unknown:
        raise SystemExit(f"unknown app(s): {', '.join(unknown)}; known: {', '.join(sorted(KNOWN_APPS))}")
    roles = [f"admin-{app}" for app in sorted(set(ARGS.app))]
    if ARGS.platform:
        roles.append(PLATFORM_ROLE)
    if not roles:
        raise SystemExit("name at least one --app or --platform")
    return roles


def ensure_roles(admin, names):
    """Role representations by name; creates missing ones (unless revoking)."""
    found = {}
    for name in names:
        try:
            found[name] = admin.call("GET", f"roles/{urllib.parse.quote(name)}")
            continue
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
        if ARGS.revoke:
            say(f"role {name!r} does not exist; nothing to revoke")
            continue
        say(f"creating role {name!r}")
        if not ARGS.dry_run:
            admin.call("POST", "roles", {"name": name})
            found[name] = admin.call("GET", f"roles/{urllib.parse.quote(name)}")
        else:
            found[name] = {"name": name}
    return found


def find_user(admin, email):
    users = admin.call("GET", f"users?email={urllib.parse.quote(email)}&exact=true") or []
    return users[0] if users else None


def ensure_user(admin, email):
    user = find_user(admin, email)
    if user:
        return user
    if ARGS.revoke:
        raise SystemExit(f"no operator with email {email!r}")

    representation = {
        "username": email,
        "email": email,
        "emailVerified": True,
        "enabled": True,
        "requiredActions": [] if ARGS.no_passkey else [PASSKEY_ACTION],
    }
    say(f"creating operator {email!r}" + ("" if ARGS.no_passkey else " (passkey required on first sign-in)"))
    if ARGS.dry_run:
        return None
    admin.call("POST", "users", representation)
    return find_user(admin, email)


def apply_roles(admin, user, roles):
    if user is None:
        say(f"would grant {', '.join(roles)}")
        return
    path = f"users/{user['id']}/role-mappings/realm"
    held = {role["name"] for role in admin.call("GET", path) or []}
    if ARGS.revoke:
        change = [roles[name] for name in roles if name in held]
        verb, method = "revoking", "DELETE"
    else:
        change = [roles[name] for name in roles if name not in held]
        verb, method = "granting", "POST"
    if not change:
        say("nothing to change")
        return
    say(f"{verb} {', '.join(role['name'] for role in change)}")
    if not ARGS.dry_run:
        admin.call(method, path, change)


def report(admin, email):
    user = find_user(admin, email)
    if user is None:
        return
    effective = admin.call("GET", f"users/{user['id']}/role-mappings/realm/composite") or []
    console = sorted(r["name"] for r in effective if r["name"].startswith("admin-") or r["name"] == PLATFORM_ROLE)
    print(f"subject {user['id']} ({email}): {', '.join(console) or 'no console roles'}")


def fetch_token():
    """Password grant on master, or client credentials for a service account."""
    client_id = os.environ.get("KC_ADMIN_CLIENT_ID")
    if client_id:
        form = {"grant_type": "client_credentials", "client_id": client_id,
                "client_secret": os.environ["KC_ADMIN_CLIENT_SECRET"]}
    else:
        form = {"grant_type": "password", "client_id": "admin-cli",
                "username": os.environ.get("KC_ADMIN_USER", "admin"),
                "password": os.environ["KC_ADMIN_PASSWORD"]}
    url = f"{KC_URL}/realms/master/protocol/openid-connect/token"
    with urlopen(url, urllib.parse.urlencode(form).encode()) as response:
        return json.load(response)["access_token"]


def main():
    names = requested_roles()
    email = ARGS.email.strip().lower()
    admin = Admin(fetch_token())
    roles = ensure_roles(admin, names)
    user = ensure_user(admin, email)
    apply_roles(admin, user, roles)
    report(admin, email)
    if ARGS.dry_run:
        print("[dry-run] nothing was changed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f"{error.code} {error.reason}: {error.read().decode()[:400]}", file=sys.stderr)
        sys.exit(1)
