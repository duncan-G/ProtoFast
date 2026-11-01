# Sign-in for a new client

Once the host is in `clientHosts`, the edge routes its sign-in paths to auth: `/signin` (which
also matches `/signin-oidc`), `/signup`, `/signout`, `/reset`, `/add-passkey` and `/account/`.
auth then picks the realm and client from the request's Host, and answers 404 for a host it
has no tenant for. Keycloak, configured by keycloak-config, must know the client, its secret
and its redirect URI. docs/05-identity.md explains the parts. `<name>`, `<NAME>` and `<Name>`
are as in the skill; `<realm>` is the realm it signs in to.

## 1. The tenant

In `services/auth/src/ProtoFast.Auth.Api/appsettings.json`, under `Tenants:ByHost`, keyed by
the host's name, not a hostname: `TenantResolver` turns the name into each environment's
hostname with `HOST_<NAME>`, so one entry serves every environment.

```json
"<name>": {
  "Realm": "<realm>",
  "ClientId": "<name>-web"
}
```

## 2. The client secret in auth and the AppHost

- `services/auth/src/ProtoFast.Auth.Api/Configuration/KeycloakOptions.cs`: a
  `ClientSecret<Name>Web` property, and `"<name>-web" => ClientSecret<Name>Web,` in
  `GetClientSecret`.
- `apphost/Program.cs`: `var <name>WebSecret = Generated("<name>-web-client-secret");` beside
  the other client secrets, and `.WithEnvironment("Keycloak__ClientSecret<Name>Web", <name>WebSecret)`
  on both `auth` and `keycloakConfig`. Locally the value is generated once and kept in the
  AppHost's user secrets; the spec lists `Keycloak__ClientSecret<Name>Web` in both components'
  `secrets`.

## 3. The realm client

In `services/keycloak-config/realms/`:

- **An existing realm:** a client in its `clients` array, copied from `theplot-web` in
  `theplot-realm.json`: `clientId` `<name>-web`, `secret` `$(env:Keycloak__ClientSecret<Name>Web)`,
  and `$(env:ORIGIN_<NAME>)` in `baseUrl`, `redirectUris` (`$(env:ORIGIN_<NAME>)/signin-oidc`),
  `webOrigins` and `post.logout.redirect.uris` (`$(env:ORIGIN_<NAME>)/*`). Keep
  `backchannel.logout.url` at `$(env:AUTH_URL)/backchannel-logout` and PKCE `S256`.
- **A realm of its own:** a copy of `theplot-realm.json` as `<realm>-realm.json`, with its
  realm name, that client, its own `account-admin` client and service-account user (the
  secret stays `$(env:Keycloak__AdminClientSecret)`), and
  `webAuthnPolicyPasswordlessRpId` `$(env:<REALM>_WEBAUTHN_RP_ID)`. Then:
  - `<realm>` in the `realms` array in `apphost/Program.cs`, which routes the realm's sign-in
    pages on the auth host;
  - `set_default <REALM>_WEBAUTHN_RP_ID "$auth"` in `set_webauthn_rp_ids` in
    `services/keycloak-config/entrypoint.sh`, so its passkeys stay out of the other realms'
    pickers;
  - `loginTheme` and `emailTheme` name a theme under `services/keycloak/themes/`; a new
    theme is a change to the keycloak image. Which theme is the developer's call.

## 4. keycloak-config's entrypoint and its tests

`services/keycloak-config/entrypoint.sh` fails a run that lacks a variable the realms need:

- `ORIGIN_<NAME>` and `Keycloak__ClientSecret<Name>Web` in `configure_login`'s `require`
  list, and in the header comment's point 3;
- `ORIGIN_<NAME>` in `trim_urls`.

`services/keycloak-config/test/entrypoint_test.sh` lists the same names: the `required`
array, the "what it requires" loop, the "every missing variable is named at once" message, the
two full runs at the end (and their secret files), and the two lists in "the real realm files
substitute only what the entrypoint requires". A new realm's `<REALM>_WEBAUTHN_RP_ID` goes in
those two lists too.

ProtoStrap sets `ORIGIN_<NAME>` in every replica, and the AppHost does under `aspire run` once
the host exists. keycloak-config reads its keys as secret files, so a key it lists that the
environment's secret lacks fails the task.

## 5. The secret in prod

Add `ClientSecret<Name>Web` to the `for client in ...` loop in `scripts/set-secrets.sh`, and
the count in its header's `clients` group. An Org Admin runs `scripts/set-secrets.sh prod`
before the release merges: it adds only the keys the secret lacks, here
`Shared_Keycloak__ClientSecret<Name>Web`.

## 6. Docs

- docs/05-identity.md: the tenants table, the realm clients, keycloak-config's variables
  (and the realms, for a new one).
- docs/04-edge.md: the auth host's row, for a new realm.
- docs/06-secrets.md: the client-secret keys, keycloak-config's secret files, the `clients`
  rotate group.
- docs/09-reference.md: auth's and keycloak-config's variables.
- docs/02-local-development.md: the generated `<name>-web-client-secret` in the local secrets.
- services/keycloak-config/README.md: its variables table.

## Check it

Locally, after restarting `aspire run` (keycloak-config's image holds the realm files):
`/signin` on `http://<name>.dev.localhost:20000` redirects to
`http://auth.dev.localhost:20000/realms/<realm>/protocol/openid-connect/auth`, the code
arrives in smtp4dev, and `/signin-oidc` sets `pf_session` and returns to the app.

- A 404 from `/signin` is a missing tenant.
- Keycloak refusing the `redirect_uri` is the realm client's `redirectUris`.
- A redirect that works and a callback that fails is the client secret: auth's and the realm
  client's differ.
- keycloak-config failing with `entrypoint: missing ...` names what step 4 or 2 left out.

In prod, after the release: `curl -sI https://<hostname>/signin | head -1` answers 302, not 404.
