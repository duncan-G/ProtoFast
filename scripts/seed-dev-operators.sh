#!/usr/bin/env bash
# Dev-only admin console operators, created (idempotently) by the AppHost once Keycloak is healthy.
# Sign in at https://localhost:20000 with the email; the code arrives in smtp4dev (localhost:8025).
# Env: KC_URL, KC_ADMIN_USER, KC_ADMIN_PASSWORD — set by the AppHost.
set -euo pipefail

grant="$(dirname "$0")/keycloak-grant-admin.py"

# Dev Keycloak serves the self-signed dev certificate, hence --insecure.
python3 "$grant" --insecure --no-passkey --app protofast --platform protofast@admin.test
python3 "$grant" --insecure --no-passkey --app theplot theplot@admin.test
