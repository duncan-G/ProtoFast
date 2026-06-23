# Keycloak realm import (prod)

This directory is the **prod** mount source for Keycloak's `--import-realm`
(`docker-compose.host-b.yml` mounts it at `/opt/keycloak/data/import`).

The canonical, hand-edited realm export lives in
[`infra/keycloak/realms/`](../../../infra/keycloak/realms/) (used by the dev
Aspire `WithRealmImport`, plan §2.2). Keep this copy in sync with it — they are
the same committed realm config (Q2), staged here so the deploy bundle that syncs
`deploy/` to the host carries the realm without depending on `infra/`.
