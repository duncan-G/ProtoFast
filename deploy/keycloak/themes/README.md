# Keycloak login theme (prod)

This directory is the **prod** mount source for Keycloak's custom themes
(`docker-compose.host-b.yml` mounts it at `/opt/keycloak/themes`, read-only).

The canonical, hand-edited theme lives in
[`infra/keycloak/themes/`](../../../infra/keycloak/themes/) (mounted by the dev
Aspire host at `/opt/keycloak/themes`). Keep this copy in sync with it — they are
the same committed theme, staged here so the deploy bundle that syncs `deploy/`
to the host carries the theme without depending on `infra/`.

The `protofast` login theme is selected via `"loginTheme": "protofast"` in the
realm import (`../realms/protofast-realm.json`).
