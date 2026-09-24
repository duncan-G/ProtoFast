# Register a new client with the auth BFF (tenant wiring)

Skip this reference if the project has no auth BFF (`services/auth/`).

Envoy routes the browser auth paths (`/signin`, `/signup`, `/signin-oidc`,
`/signout`, `/reset`, `/add-passkey`) to the auth service on **every**
client vhost automatically (`proxy/envoy.vhost.yaml.tmpl`). But the auth
service resolves the tenant from the request's `Host` header and returns
**404 for any host not in its tenant map** (`AuthFlow` →
`TenantResolver`). A new client whose tenant is not registered will serve
its pages fine and 404 on `/signup` — the failure only shows up in the
environment whose map you forgot.

Decide first: does the client join an **existing realm** (like `admin`
sharing `protofast`) or get its **own realm** (like `theplot`)? Every step
below applies either way; a new realm additionally needs the realm import
(step 3).

## 1. Dev tenant map

`services/auth/src/ProtoFast.Auth.Api/appsettings.Development.json` →
`Tenants:ByHost`. The key is the client's per-client Envoy listener,
`localhost+«port»` (20000, 20001, … in `WithClient` registration order).
The `+` stands in for `:` — a colon in a .NET config key is a path
separator and silently unbinds the entry (a binding test in
`TenantResolverTests` covers this).

```jsonc
"localhost+«port»": {
  "Realm": "«realm»",
  "ClientId": "«clientname»-web"
}
```

## 2. Client-secret plumbing (confidential client)

- `services/auth/src/ProtoFast.Auth.Api/Configuration/KeycloakOptions.cs`:
  add a `ClientSecret«ClientName»Web` property and its arm in the
  `GetClientSecret` switch.
- `deploy/deploy.sh` `ensure_secret_files`: add the seeding line that
  copies `Auth_Keycloak__ClientSecret«ClientName»Web` from the
  `«project»/app` Secrets Manager secret into `.env` as
  `«CLIENTNAME»_WEB_CLIENT_SECRET` (mirror the existing lines).
- Add the `Auth_Keycloak__ClientSecret«ClientName»Web` key **to the SM
  secret itself** before the first deploy — the seeding line is
  best-effort and silently leaves the var empty when the key is absent,
  which breaks the `/signin-oidc` code exchange while the redirect
  appears to work.

## 3. Keycloak realm import (own-realm clients only)

- Realm JSON in `deploy/keycloak/realms/«realm»-realm.json` **and** the
  `infra/keycloak/realms/` copy, using `${env.*}` placeholders like the
  existing realms.
- The **keycloak** service env in `deploy/docker-compose.host-services.yml`:
  the `«CLIENTNAME»_WEB_CLIENT_SECRET` and `«CLIENTNAME»_WEB_BASE_URL`
  placeholder values the import substitutes.

## 4. Auth service production env — the easy one to miss

In `deploy/docker-compose.host-services.yml`, the **auth** service's
`environment` (a different block from keycloak's — updating one does not
update the other):

```yaml
Auth_Keycloak__ClientSecret«ClientName»Web: "${«CLIENTNAME»_WEB_CLIENT_SECRET}"
# ...
Tenants__ByHost__«clientdomain»__Realm: «realm»
Tenants__ByHost__«clientdomain»__ClientId: «clientname»-web
```

Without the `Tenants__ByHost` pair, production `/signup` on the new
domain 404s (dev keeps working off `appsettings.Development.json`, so
nothing catches it before deploy). Without the secret line, the redirect
works but the callback's token exchange fails.

## 5. Docs

Record the tenant in `docs/05-identity.md` alongside the existing ones.

## Verify

Dev: sign-up/sign-in through the client's Envoy listener URL lands on the
right realm's login page. Prod, after the services host redeploys:

```bash
curl -sI https://«clientdomain»/signup | head -1   # expect 302 to the auth domain, not 404
```
