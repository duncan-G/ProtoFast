#!/usr/bin/env python3
"""Bring a LIVE realm's browser-client URLs in line with the committed realm export.

`--import-realm` only ever CREATES realms, so a change to a client's redirect URIs,
web origins, base URL or post-logout redirect URIs never reaches a realm that already
exists — and in dev the realms live in the persistent `keycloak` database. A stale
redirect URI fails every sign-in with `invalid_redirect_uri`.

For every client in every `*-realm.json` under the realms directory that has redirect
URIs, this sets exactly those four fields on the live client, and nothing else.
`${VAR:default}` placeholders resolve from the environment, else the default, the way
Keycloak's import does. Realms or clients missing from the live server are skipped:
the import creates those itself.

The AppHost runs this on every dev start (idempotent). Usage by hand:

    KC_URL=https://localhost:8080 KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=... \\
        scripts/keycloak-apply-client-urls.py --insecure infra/keycloak/realms

Pass --dry-run to see what it would change. Pass --insecure to skip TLS verification,
which the Aspire dev stack needs (self-signed dev certificate) and a real deployment
must never need.
"""

import argparse
import json
import os
import re
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

KC_URL = os.environ.get("KC_URL", "http://localhost:8080").rstrip("/")
POST_LOGOUT = "post.logout.redirect.uris"
PLACEHOLDER = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::([^}]*))?\}")

parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
parser.add_argument("realms_dir", type=Path)
parser.add_argument("--dry-run", action="store_true")
parser.add_argument("--insecure", action="store_true", help="skip TLS verification (dev only)")
ARGS = parser.parse_args()

TLS = ssl._create_unverified_context() if ARGS.insecure else None


def urlopen(url, data=None):
    return urllib.request.urlopen(url, data, context=TLS)


def say(message):
    print(("[dry-run] " if ARGS.dry_run else "") + message)


def resolve(value):
    if isinstance(value, str):
        return PLACEHOLDER.sub(lambda m: os.environ.get(m.group(1), m.group(2) or ""), value)
    if isinstance(value, list):
        return [resolve(v) for v in value]
    return value


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


def call(token, method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{KC_URL}/admin/realms/{path}", data, method=method)
    req.add_header("Authorization", f"Bearer {token}")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urlopen(req) as response:
            raw = response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise
    return json.loads(raw) if raw else None


def comparable(value):
    # Keycloak stores the URI lists as sets and hands them back in its own order.
    return sorted(value) if isinstance(value, list) else (value or "")


def apply_client(token, realm, wanted):
    client_id = wanted["clientId"]
    found = call(token, "GET", f"{urllib.parse.quote(realm)}/clients?clientId={urllib.parse.quote(client_id)}")
    if not found:
        say(f"{realm}/{client_id}: not on the server, skipping")
        return
    live = found[0]

    desired = {
        "redirectUris": resolve(wanted.get("redirectUris", [])),
        "webOrigins": resolve(wanted.get("webOrigins", [])),
        "baseUrl": resolve(wanted.get("baseUrl", "")),
    }
    post_logout = resolve(wanted.get("attributes", {}).get(POST_LOGOUT, ""))

    changes = {k: v for k, v in desired.items() if comparable(live.get(k)) != comparable(v)}
    if live.get("attributes", {}).get(POST_LOGOUT, "") != post_logout:
        changes["attributes"] = {POST_LOGOUT: post_logout}

    if not changes:
        say(f"{realm}/{client_id}: up to date")
        return

    say(f"{realm}/{client_id}: setting {', '.join(sorted(changes))}")
    if not ARGS.dry_run:
        # A partial representation: Keycloak updates only the fields present, and merges
        # attributes rather than replacing the map.
        call(token, "PUT", f"{urllib.parse.quote(realm)}/clients/{live['id']}", changes)


def main():
    token = fetch_token()
    for path in sorted(ARGS.realms_dir.glob("*-realm.json")):
        export = json.loads(path.read_text())
        realm = export["realm"]
        if call(token, "GET", urllib.parse.quote(realm)) is None:
            say(f"{realm}: not on the server, skipping")
            continue
        for client in export.get("clients", []):
            if client.get("redirectUris"):
                apply_client(token, realm, client)

    print("[dry-run] nothing was changed" if ARGS.dry_run else "done")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except urllib.error.HTTPError as error:
        print(f"{error.code} {error.reason}: {error.read().decode()[:400]}", file=sys.stderr)
        sys.exit(1)
