#!/usr/bin/env python3
"""Move a LIVE protofast realm onto the passwordless browser and registration flows.

`kc.sh start --import-realm` only ever CREATES realms, so the flow change in
`deploy/keycloak/realms/protofast-realm.json` never reaches a realm that already
exists. deploy.sh reconciles realm flags, required actions and a couple of client
settings through the Admin API, but deliberately leaves authentication flows alone
— a bound flow half-rewritten by a best-effort deploy step is how you lock every
user out. This is the deliberate, watched, run-it-yourself half.

What it builds, to match the realm JSON:

    passwordless browser                          (bound as browserFlow)
      |- Cookie                                   ALTERNATIVE
      |- Identity Provider Redirector             ALTERNATIVE
      \\- passwordless forms                       ALTERNATIVE
           |- passwordless sign-in                CONDITIONAL
           |    |- Condition - Level of Authentication  (level 1)
           |    |- Username Form                  REQUIRED
           |    \\- passwordless credentials       REQUIRED
           |         |- WebAuthn Passwordless     ALTERNATIVE
           |         \\- Email Code                ALTERNATIVE
           \\- passwordless step-up                CONDITIONAL
                |- Condition - Level of Authentication  (level 2, max age 900)
                \\- WebAuthn Passwordless          REQUIRED

    passwordless registration                     (bound as registrationFlow)
      \\- passwordless registration form
           \\- Registration User Profile Creation

Three things about that shape are load-bearing and easy to get wrong by hand:

* The two branches under `passwordless forms` are CONDITIONAL and there is nothing
  ALTERNATIVE beside them. Keycloak discards every ALTERNATIVE that shares a level
  with a REQUIRED or CONDITIONAL sibling — put the step-up branch next to Cookie at
  the top level and cookie SSO and the identity-provider redirect stop working.
* The level-1 condition is not decoration. An authentication that has not yet
  reached any level matches *every* level condition, so without it the step-up
  branch would fire on everyone's first sign-in and demand a passkey.
* `registration-password-action` is absent from the registration form rather than
  disabled. It attaches an UPDATE_PASSWORD required action to every new user
  regardless of whether the realm has that action enabled, and a disabled action
  never runs — so it would sit pending on every account forever.

It also registers the `verify-email-code` required action. A provider that ships in
a new JAR is not registered on an existing realm automatically; until it is, the
realm has no such action and deploy.sh's reconcile can only warn about it.

Order matters: everything is built and populated BEFORE the realm is rebound, and
the old flows are deleted only after the rebind succeeds. An interrupted run leaves
a realm that still signs people in. Re-running is a no-op.

Requires the `email-otp` authenticator to exist on the server — that is the
provider JAR in deploy/keycloak/providers/. Without it Keycloak rejects the
execution and the run stops before anything is rebound.

Usage (dev, admin/admin on a local container):

    KC_URL=http://localhost:8080 KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=admin \\
        scripts/keycloak-apply-passwordless-flow.py

Usage (prod, where nothing sets KC_BOOTSTRAP_ADMIN_* and there is no standing
admin account — mint a throwaway service account the way deploy.sh does, run this,
then delete the client from the master realm again):

    docker compose exec keycloak /opt/keycloak/bin/kc.sh bootstrap-admin service \\
        --client-id protofast-flow-apply --client-secret:env SECRET --no-prompt
    KC_URL=https://auth.protofast.dev \\
    KC_ADMIN_CLIENT_ID=protofast-flow-apply KC_ADMIN_CLIENT_SECRET="$SECRET" \\
        scripts/keycloak-apply-passwordless-flow.py

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

BROWSER_FLOW = "passwordless browser"
REGISTRATION_FLOW = "passwordless registration"

# Retired by this script, in this order. Only the top-level flow needs naming —
# deleting it takes its sub-flows and their authenticator configs with it.
OBSOLETE_TOP_FLOWS = ["passkey-or-password browser"]

REQUIRED_ACTION = ("verify-email-code", "Verify Email by Code")

# ACR value -> level. The admin client asks for "passkey"; nothing else asks for
# anything, which is what keeps the step-up branch out of everyone else's way.
ACR_LOA_MAP = {"basic": 1, "passkey": 2}

# Level 1 is held for the life of an SSO session (ssoSessionMaxLifespan), so a
# step-up inside a live session re-proves the passkey alone instead of sending the
# user back through the whole sign-in. Level 2 expires in 15 minutes, which is what
# makes the admin gate re-prompt rather than being satisfied once and forgotten.
BASIC_LOA_CONFIG = {"loa-condition-level": "1", "loa-max-age": "604800"}
PASSKEY_LOA_CONFIG = {"loa-condition-level": "2", "loa-max-age": "900"}

DRY_RUN = "--dry-run" in sys.argv


def say(message):
    print(("[dry-run] " if DRY_RUN else "") + message)


class Node:
    """One row in a flow tree."""

    def __init__(self, provider, display_name, requirement, config=None):
        self.provider = provider
        self.display_name = display_name
        self.requirement = requirement
        self.config = config


class Sub(Node):
    """A sub-flow row, plus the rows it contains."""

    def __init__(self, alias, description, requirement, children,
                 flow_type="basic-flow", provider=None):
        super().__init__(provider, alias, requirement)
        self.alias = alias
        self.description = description
        self.children = children
        self.flow_type = flow_type


# Display names are Keycloak's, not ours: the executions endpoint reports rows by
# the provider's display type, and that is the only handle a row has before it is
# given a requirement.
BROWSER_TREE = [
    Node("auth-cookie", "Cookie", "ALTERNATIVE"),
    Node("identity-provider-redirector", "Identity Provider Redirector", "ALTERNATIVE"),
    Sub("passwordless forms",
        "Branches keyed on the level of authentication the client asked for",
        "ALTERNATIVE",
        [
            Sub("passwordless sign-in",
                "Ordinary sign-in. The level-1 condition is what stamps a level on "
                "the session.",
                "CONDITIONAL",
                [
                    Node("conditional-level-of-authentication",
                         "Condition - Level of Authentication", "REQUIRED",
                         config=("basic-level-of-authentication", BASIC_LOA_CONFIG)),
                    Node("auth-username-form", "Username Form", "REQUIRED"),
                    Sub("passwordless credentials",
                        "Passkey if this device has one, otherwise a mailed code",
                        "REQUIRED",
                        [
                            Node("webauthn-authenticator-passwordless",
                                 "WebAuthn Passwordless Authenticator", "ALTERNATIVE"),
                            Node("email-otp", "Email Code", "ALTERNATIVE"),
                        ]),
                ]),
            Sub("passwordless step-up",
                "Runs only for a client that asked for the passkey level",
                "CONDITIONAL",
                [
                    Node("conditional-level-of-authentication",
                         "Condition - Level of Authentication", "REQUIRED",
                         config=("passkey-level-of-authentication", PASSKEY_LOA_CONFIG)),
                    Node("webauthn-authenticator-passwordless",
                         "WebAuthn Passwordless Authenticator", "REQUIRED"),
                ]),
        ]),
]

REGISTRATION_TREE = [
    Sub("passwordless registration form",
        "Email address only; no password step",
        "REQUIRED",
        [
            Node("registration-user-creation",
                 "Registration User Profile Creation", "REQUIRED"),
        ],
        flow_type="form-flow",
        provider="registration-page-form"),
]


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

    def executions(self, top_alias):
        return self.call("GET", self.flow(top_alias, "/executions"))

    def top_level_flows(self):
        return self.call("GET", "authentication/flows")


def children(execs, parent):
    """The rows one level under `parent`, in order.

    Execution rows carry no parent id — only `level` and their position in the
    flattened list — so a sub-flow's contents are the rows that follow it until the
    tree pops back up to its own level.
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


def rows_under(admin, top_alias, parent_alias):
    """Fresh read of the rows directly under `parent_alias` within `top_alias`."""
    execs = admin.executions(top_alias)
    if parent_alias == top_alias:
        return [r for r in execs if r["level"] == 0]
    parent = row_named(execs, parent_alias)
    if parent is None:
        raise SystemExit(f"sub-flow {parent_alias!r} is missing from {top_alias!r}")
    return children(execs, parent)


def set_requirement(admin, top_alias, parent_alias, display_name, requirement):
    """Requirements are written back through the flow that CONTAINS the row."""
    row = row_named(rows_under(admin, top_alias, parent_alias), display_name)
    if row is None:
        raise SystemExit(f"{display_name!r} is not under {parent_alias!r}")
    if row.get("requirement") == requirement:
        return row
    row["requirement"] = requirement
    admin.call("PUT", admin.flow(parent_alias, "/executions"), row)
    return row


def add_node(admin, top_alias, parent_alias, node):
    """Create one row under `parent_alias` if it is not already there."""
    existing = row_named(rows_under(admin, top_alias, parent_alias), node.display_name) \
        if not DRY_RUN else None

    if isinstance(node, Sub):
        if existing is None:
            say(f"  create sub-flow {node.alias!r} under {parent_alias!r} ({node.requirement})")
            if not DRY_RUN:
                admin.call("POST", admin.flow(parent_alias, "/executions/flow"), {
                    "alias": node.alias,
                    "type": node.flow_type,
                    "description": node.description,
                    # The server only reads `provider` when the new flow is a
                    # form-flow, which is exactly when we have one to give it.
                    "provider": node.provider or "registration-page-form",
                })
        for child in node.children:
            add_node(admin, top_alias, node.alias, child)
    else:
        if existing is None:
            say(f"  add {node.display_name!r} to {parent_alias!r} ({node.requirement})")
            if not DRY_RUN:
                admin.call("POST", admin.flow(parent_alias, "/executions/execution"),
                           {"provider": node.provider})

    if DRY_RUN:
        return

    row = set_requirement(admin, top_alias, parent_alias, node.display_name, node.requirement)

    if node.config and not row.get("authenticationConfig"):
        alias, config = node.config
        say(f"  configure {node.display_name!r} under {parent_alias!r}: {config}")
        admin.call("POST", f"authentication/executions/{row['id']}/config",
                   {"alias": alias, "config": config})


def build_flow(admin, alias, description, tree):
    """Create the top-level flow if absent, then fill it in. Idempotent."""
    if DRY_RUN:
        exists = any(f["alias"] == alias for f in admin.top_level_flows())
        say(f"top-level flow {alias!r}: {'already exists' if exists else 'create'}")
    else:
        if not any(f["alias"] == alias for f in admin.top_level_flows()):
            say(f"create top-level flow {alias!r}")
            admin.call("POST", "authentication/flows", {
                "alias": alias,
                "description": description,
                "providerId": "basic-flow",
                "topLevel": True,
                "builtIn": False,
            })

    for node in tree:
        add_node(admin, alias, alias, node)


def register_required_action(admin):
    alias, name = REQUIRED_ACTION
    if not DRY_RUN:
        registered = admin.call("GET", "authentication/required-actions")
        if any(a["alias"] == alias for a in registered):
            say(f"required action {alias!r} already registered")
            return
    say(f"register required action {alias!r}")
    if not DRY_RUN:
        admin.call("POST", "authentication/register-required-action",
                   {"providerId": alias, "name": name})


def bind_and_configure(admin):
    """Point the realm at the new flows and publish the ACR map, in one write."""
    say(f"bind browserFlow={BROWSER_FLOW!r}, registrationFlow={REGISTRATION_FLOW!r}, "
        f"acr.loa.map={ACR_LOA_MAP}")
    if DRY_RUN:
        return
    # Read-modify-write the attribute map: a PUT that carries `attributes` replaces
    # it wholesale, and the action-token lifespans live in there too.
    current = admin.call("GET", "")
    attributes = dict(current.get("attributes") or {})
    attributes["acr.loa.map"] = json.dumps(ACR_LOA_MAP, separators=(",", ":"))
    admin.call("PUT", "", {
        "realm": REALM,
        "browserFlow": BROWSER_FLOW,
        "registrationFlow": REGISTRATION_FLOW,
        "attributes": attributes,
    })


def delete_obsolete(admin):
    flows = {f["alias"]: f for f in admin.top_level_flows()}
    for alias in OBSOLETE_TOP_FLOWS:
        flow = flows.get(alias)
        if flow is None:
            continue
        say(f"delete retired flow {alias!r} (and everything under it)")
        if not DRY_RUN:
            admin.call("DELETE", f"authentication/flows/{flow['id']}")


def assert_provider_present(admin):
    """Fail before touching anything if the provider JAR is not loaded."""
    providers = admin.call("GET", "authentication/authenticator-providers")
    if not any(p.get("id") == "email-otp" for p in providers):
        raise SystemExit(
            "the 'email-otp' authenticator is not on this server. Deploy the provider "
            "JAR (deploy/keycloak/providers/protofast-keycloak.jar) and restart "
            "Keycloak before running this."
        )


def main():
    admin = Admin(fetch_token())
    assert_provider_present(admin)

    register_required_action(admin)

    say(f"realm {REALM!r}: building {BROWSER_FLOW!r}")
    build_flow(admin, BROWSER_FLOW,
               "Email-first browser flow: passkey or a mailed code, never a password",
               BROWSER_TREE)

    say(f"realm {REALM!r}: building {REGISTRATION_FLOW!r}")
    build_flow(admin, REGISTRATION_FLOW, "Registration with no password step",
               REGISTRATION_TREE)

    # Only now that both flows exist and are fully populated.
    bind_and_configure(admin)
    delete_obsolete(admin)

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
