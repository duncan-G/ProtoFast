# Database secrets (§4.4). Generated here, never baked into an image or compose
# file. Host B's user_data writes each to a root-only (0600) file under
# /opt/protofast that the compose file-secrets / init script read:
#   - kc-db-password   -> Postgres superuser + Keycloak's KC_DB_PASSWORD
#   - auth-db-password -> auth's `auth` role; ALSO injected (same value)
#                         into auth's connection string via AUTH_DB_PASSWORD
#                         in .env (keep the two in sync, §3.2).
# special=false: these flow through a Postgres connection string and a JDBC URL;
# avoiding shell/URL metacharacters keeps both unambiguous.

resource "random_password" "kc_db" {
  length  = 32
  special = false
}

resource "random_password" "auth_db" {
  length  = 32
  special = false
}
