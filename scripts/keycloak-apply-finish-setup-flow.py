#!/usr/bin/env python3
"""Add the finish-account-setup branch to a LIVE protofast realm's browser flow.

`kc.sh start --import-realm` only ever CREATES realms, so the browser-flow change
in `deploy/keycloak/realms/protofast-realm.json` never reaches a realm that
already exists — the same reason deploy.sh has to reconcile realm flags and
required actions through the Admin API. deploy.sh deliberately leaves
authentication flows alone (a bound flow half-rewritten by a best-effort deploy
step is how you lock every user out), so this is the deliberate, watched,
run-it-yourself half.

What it builds, to match the realm JSON:

    passkey-or-password credentials
      |- passkey-or-password stored credential   CONDITIONAL
      |    |- Condition - user configured        REQUIRED
      |    |- WebAuthn Passwordless              ALTERNATIVE
      |    \\- Password Form                      ALTERNATIVE
      \\- finish account setup                    CONDITIONAL
           |- Condition - sub-flow executed      REQUIRED  (stored-credential branch skipped)
           \\- Send Reset Email                   REQUIRED

Sign-up creates the account before any credential exists
(registration-password-action defers the password past email verification), so
anyone who walks away at the verify-email or set-password step leaves an account
behind that has nothing to authenticate with. The old flow could only answer
"Invalid username or password" for those, forever. The second branch emails them
a link to finish instead.

Order matters: both branches are built and populated BEFORE the two original
executions are removed, so an interrupted run still leaves a browser flow that
signs people in. Re-running is a no-op.

Usage (dev, admin/admin on a local container):

    KC_URL=http://localhost:8080 KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=admin \\
        scripts/keycloak-apply-finish-setup-flow.py

Usage (prod, where nothing sets KC_BOOTSTRAP_ADMIN_* and there is no standing
admin account — mint a throwaway service account the way deploy.sh does, run
this, then delete the client from the master realm again):

    docker compose exec keycloak /opt/keycloak/bin/kc.sh bootstrap-admin service \\
        --client-id protofast-flow-apply --client-secret:env SECRET --no-prompt
    KC_URL=https://auth.protofast.dev \\
    KC_ADMIN_CLIENT_ID=protofast-flow-apply KC_ADMIN_CLIENT_SECRET="$SECRET" \\
        scripts/keycloak-apply-finish-setup-flow.py

Pass --dry-run to print the plan without touching the realm.
"""

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

KC_URL = os.environ.get("KC_URL", "http://localhost:8080").rstrip("/")
REALM = os.environ.get("KC_REALM", "protofast")

TOP_FLOW = "passkey-or-password browser"
PARENT = "passkey-or-password credentials"
STORED = "passkey-or-password stored credential"
FINISH = "finish account setup"
CONFIG_ALIAS = "credential-branch-skipped"

# Realm attributes the same change carries. Keycloak's default action-token
# lifespan is 5 minutes, which is nothing for "check your email and come back" —
# every link in this flow is one somebody opens later.
ATTRIBUTES = {
    "actionTokenGeneratedByUserLifespan.verify-email": "43200",
    "actionTokenGeneratedByUserLifespan.reset-credentials": "3600",
}

DRY_RUN = "--dry-run" in sys.argv


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
        with urllib.request.urlopen(req) as response:
            raw = response.read()
        return json.loads(raw) if raw else None

    def flow(self, alias, suffix=""):
        return f"authentication/flows/{urllib.parse.quote(alias)}{suffix}"

    def executions(self):
        return self.call("GET", self.flow(TOP_FLOW, "/executions"))


def children(execs, parent):
    """The rows one level under `parent`, in order.

    Execution rows carry no parent id — only `level` and their position in the
    flattened list — so a sub-flow's contents are the rows that follow it until
    the tree pops back up to its own level.
    """
    kids = []
    for row in execs[execs.index(parent) + 1:]:
        if row["level"] <= parent["level"]:
            break
        if row["level"] == parent["level"] + 1:
            kids.append(row)
    return kids


def row_named(rows, display_name):
    return next((r for r in rows if r.get("displayName") == display_name), None)


def add_subflow(admin, parent_alias, alias, description, requirement):
    say(f"  create sub-flow {alias!r} under {parent_alias!r} ({requirement})")
    if DRY_RUN:
        return
    admin.call("POST", admin.flow(parent_alias, "/executions/flow"), {
        "alias": alias,
        "type": "basic-flow",
        "description": description,
        # The admin console sends this for every sub-flow it creates; the server
        # only reads it when the new flow is a form-flow, which this is not.
        "provider": "registration-page-form",
    })
    set_requirement(admin, parent_alias, alias, requirement)


def add_execution(admin, flow_alias, provider, display_name, requirement):
    say(f"  add {display_name!r} to {flow_alias!r} ({requirement})")
    if DRY_RUN:
        return
    admin.call("POST", admin.flow(flow_alias, "/executions/execution"),
               {"provider": provider})
    return set_requirement(admin, flow_alias, display_name, requirement)


def set_requirement(admin, flow_alias, display_name, requirement):
    """Requirements are written back through the flow that CONTAINS the row."""
    row = locate(admin, flow_alias, display_name)
    row["requirement"] = requirement
    admin.call("PUT", admin.flow(flow_alias, "/executions"), row)
    return row


def locate(admin, flow_alias, display_name):
    """Re-read the tree and find `display_name` directly under `flow_alias`.

    Always a fresh read: the same display name can appear in more than one branch
    while the rewrite is half-done, and only the parent tells them apart.
    """
    execs = admin.executions()
    parent = row_named(execs, flow_alias)
    if parent is None:
        raise SystemExit(f"sub-flow {flow_alias!r} vanished from {TOP_FLOW!r}")
    row = row_named(children(execs, parent), display_name)
    if row is None:
        raise SystemExit(f"{display_name!r} is not under {flow_alias!r}")
    return row


def main():
    admin = Admin(fetch_token())
    execs = admin.executions()

    if row_named(execs, FINISH):
        print(f"realm {REALM!r}: {FINISH!r} already present — nothing to do")
        return 0

    parent = row_named(execs, PARENT)
    if parent is None:
        print(f"realm {REALM!r}: {TOP_FLOW!r} has no {PARENT!r} sub-flow — "
              "is this the protofast browser flow?", file=sys.stderr)
        return 1

    # The rows that move down a level. Captured BEFORE anything is created, so
    # the copies made below (same display names) can never be confused for them.
    doomed = [r for r in children(execs, parent) if not r.get("authenticationFlow")]
    say(f"realm {REALM!r}: rewriting {PARENT!r} — "
        f"{len(doomed)} execution(s) move into a conditional branch")

    add_subflow(admin, PARENT, STORED,
                "Passkey (WebAuthn passwordless) or password. "
                "Skipped when the account has neither.", "CONDITIONAL")
    add_execution(admin, STORED, "conditional-user-configured",
                  "Condition - user configured", "REQUIRED")
    add_execution(admin, STORED, "webauthn-authenticator-passwordless",
                  "WebAuthn Passwordless Authenticator", "ALTERNATIVE")
    add_execution(admin, STORED, "auth-password-form", "Password Form", "ALTERNATIVE")

    add_subflow(admin, PARENT, FINISH,
                "Half-registered account (no passkey, no password): "
                "email a link to set the first one.", "CONDITIONAL")
    condition = add_execution(admin, FINISH, "conditional-sub-flow-executed",
                              "Condition - sub-flow executed", "REQUIRED")
    say(f"  configure that condition: {STORED!r} not-executed")
    if not DRY_RUN:
        admin.call("POST", f"authentication/executions/{condition['id']}/config",
                   {"alias": CONFIG_ALIAS,
                    "config": {"flow_to_check": STORED, "check_result": "not-executed"}})
    add_execution(admin, FINISH, "reset-credential-email", "Send Reset Email", "REQUIRED")

    # Only now that both branches exist and are wired: drop the originals.
    for row in doomed:
        say(f"  remove {row.get('displayName')!r} from {PARENT!r} "
            f"(it now lives in {STORED!r})")
        if not DRY_RUN:
            admin.call("DELETE", f"authentication/executions/{row['id']}")

    lifespans = ", ".join(f"{k.rsplit('.', 1)[-1]}={v}s" for k, v in ATTRIBUTES.items())
    say(f"realm {REALM!r}: action-token lifespans {lifespans}")
    if not DRY_RUN:
        admin.call("PUT", "", {"realm": REALM, "attributes": ATTRIBUTES})

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
    with urllib.request.urlopen(url, urllib.parse.urlencode(form).encode()) as response:
        return json.load(response)["access_token"]


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f"{error.code} {error.reason}: {error.read().decode()[:400]}", file=sys.stderr)
        sys.exit(1)
