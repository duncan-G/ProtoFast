#!/usr/bin/env python3
"""Create the `account-admin` service-account client on a LIVE protofast realm.

`kc.sh start --import-realm` only ever CREATES realms, and deploy.sh's reconcile
deliberately never creates or rewrites clients — so the client that account
management needs (`infra/keycloak/realms/protofast-realm.json`) never reaches a
realm that already exists. This is the deliberate, run-it-yourself half.

What it does, idempotently:

* creates the confidential client `account-admin` — service accounts on, every
  browser flow off, so it can do nothing but the client-credentials grant;
* sets its secret to the value you pass, when you pass one;
* grants its service-account user `view-users` and `manage-users` on
  `realm-management`, and nothing else.

Those two roles are the whole of the standing admin credential in this system.
auth-svc uses them for three calls a user makes about their own account — list my
passkeys, remove one, delete my account — none of which any OIDC flow expresses.
No sign-in path touches this client: whether an account has a passkey is answered
by `UserAccount.PasskeyRegisteredAt` and the `amr` claim (credential plan §2.5).

Re-running is a no-op apart from the secret, which is rewritten whenever
ACCOUNT_ADMIN_CLIENT_SECRET is set. Keep that value the same as the one auth-svc
reads from `Auth_Keycloak__AdminClientSecret` — they are the two halves of one
credential, and a mismatch surfaces as 503s on the account page.

Usage (dev, admin/admin on a local container):

    KC_URL=http://localhost:8080 KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=admin \\
    ACCOUNT_ADMIN_CLIENT_SECRET=dev-account-admin-secret \\
        scripts/keycloak-apply-account-admin-client.py

Usage (prod, where nothing sets KC_BOOTSTRAP_ADMIN_* and there is no standing admin
account — mint a throwaway service account the way deploy.sh does, run this, then
delete the client from the master realm again):

    docker compose exec keycloak /opt/keycloak/bin/kc.sh bootstrap-admin service \\
        --client-id protofast-account-apply --client-secret:env SECRET --no-prompt
    KC_URL=https://auth.protofast.dev \\
    KC_ADMIN_CLIENT_ID=protofast-account-apply KC_ADMIN_CLIENT_SECRET="$SECRET" \\
    ACCOUNT_ADMIN_CLIENT_SECRET="$(aws secretsmanager get-secret-value --secret-id protofast/app \\
        --query SecretString --output text | jq -r .Auth_Keycloak__AdminClientSecret)" \\
        scripts/keycloak-apply-account-admin-client.py

Pass --dry-run to see what it would change. Pass --insecure to skip TLS verification,
which the Aspire dev stack needs (Keycloak serves HTTPS there with the self-signed dev
certificate) and which a real deployment must never need.
"""

import json
import os
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request

KC_URL = os.environ.get("KC_URL", "http://localhost:8080").rstrip("/")
REALM = os.environ.get("KC_REALM", "protofast")

CLIENT_ID = os.environ.get("ACCOUNT_ADMIN_CLIENT_ID", "account-admin")
CLIENT_SECRET = os.environ.get("ACCOUNT_ADMIN_CLIENT_SECRET")

# The whole grant. Widening this list is a decision about blast radius, not a detail.
ROLES = ["view-users", "manage-users"]

DRY_RUN = "--dry-run" in sys.argv

# Dev only: the Aspire stack fronts Keycloak with the self-signed ASP.NET dev certificate,
# which nothing on the host trusts. Never pass --insecure at a real deployment.
TLS = ssl._create_unverified_context() if "--insecure" in sys.argv else None


def urlopen(url, data=None):
    return urllib.request.urlopen(url, data, context=TLS)


def say(message):
    print(("[dry-run] " if DRY_RUN else "") + message)


class Admin:
    """Just enough of the Admin API, against one realm."""

    def __init__(self, token):
        self.token = token

    def call(self, method, path, body=None):
        url = f"{KC_URL}/admin/realms/{urllib.parse.quote(REALM)}"
        if path:
            url += "/" + path
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data, method=method)
        req.add_header("Authorization", f"Bearer {self.token}")
        if data is not None:
            req.add_header("Content-Type", "application/json")
        with urlopen(req) as response:
            raw = response.read()
        return json.loads(raw) if raw else None


def find_client(admin, client_id):
    found = admin.call("GET", f"clients?clientId={urllib.parse.quote(client_id)}")
    return found[0] if found else None


def ensure_client(admin):
    """The client itself. Returns its internal id, or None on a dry run that would create it."""
    existing = find_client(admin, CLIENT_ID)
    if existing:
        say(f"client {CLIENT_ID!r} already exists")

        # Full scope is repaired on every run, not just at creation: without it the service
        # account's token carries none of the roles below and every Admin API call is a 403.
        updates = {"fullScopeAllowed": True}
        if CLIENT_SECRET:
            updates["secret"] = CLIENT_SECRET
        else:
            say(f"client {CLIENT_ID!r}: ACCOUNT_ADMIN_CLIENT_SECRET not set, leaving the secret alone")

        say(f"client {CLIENT_ID!r}: setting {', '.join(sorted(updates))}")
        if not DRY_RUN:
            admin.call("PUT", f"clients/{existing['id']}", updates)
        return existing["id"]

    representation = {
        "clientId": CLIENT_ID,
        "name": "ProtoFast Account Administration",
        "description": "Service account auth-svc uses for the Admin API calls account "
                       "management needs: read a user's passkeys, remove one, delete the account.",
        "enabled": True,
        "protocol": "openid-connect",
        "publicClient": False,
        "clientAuthenticatorType": "client-secret",
        "standardFlowEnabled": False,
        "implicitFlowEnabled": False,
        "directAccessGrantsEnabled": False,
        "serviceAccountsEnabled": True,
        # The service account's token has to CARRY the two roles granted below, and a client
        # with full scope off strips every role it has not been given a scope mapping for —
        # leaving a token the Admin API answers 403 to. This client holds nothing but those
        # two roles, so full scope is exactly that grant and no more.
        "fullScopeAllowed": True,
        "redirectUris": [],
        "webOrigins": [],
    }
    if CLIENT_SECRET:
        representation["secret"] = CLIENT_SECRET
    else:
        say("WARNING: ACCOUNT_ADMIN_CLIENT_SECRET is not set; Keycloak will generate a secret "
            "and auth-svc will not know it")

    say(f"creating client {CLIENT_ID!r}")
    if DRY_RUN:
        return None

    admin.call("POST", "clients", representation)
    return find_client(admin, CLIENT_ID)["id"]


def ensure_roles(admin, client_uuid):
    """`view-users` + `manage-users` on the service-account user, and nothing else."""
    if client_uuid is None:
        say(f"would grant {', '.join(ROLES)} on realm-management to the service account")
        return

    service_account = admin.call("GET", f"clients/{client_uuid}/service-account-user")
    manager = find_client(admin, "realm-management")
    if manager is None:
        raise SystemExit("realm-management client not found; is KC_REALM right?")

    path = f"users/{service_account['id']}/role-mappings/clients/{manager['id']}"
    held = {role["name"] for role in admin.call("GET", path) or []}
    missing = [role for role in ROLES if role not in held]
    if not missing:
        say(f"service account already holds {', '.join(ROLES)}")
        return

    available = {role["name"]: role for role in admin.call("GET", path + "/available") or []}
    grant = [available[name] for name in missing if name in available]
    unknown = [name for name in missing if name not in available]
    if unknown:
        raise SystemExit(f"realm-management has no role(s): {', '.join(unknown)}")

    say(f"granting {', '.join(missing)} on realm-management")
    if not DRY_RUN:
        admin.call("POST", path, grant)


def main():
    admin = Admin(fetch_token())
    client_uuid = ensure_client(admin)
    ensure_roles(admin, client_uuid)

    print("[dry-run] nothing was changed" if DRY_RUN else "done")
    return 0


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


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f"{error.code} {error.reason}: {error.read().decode()[:400]}", file=sys.stderr)
        sys.exit(1)
