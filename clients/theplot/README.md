# ThePlot client

The ThePlot web client — landing page, sign-up/sign-in entry points, and
account management for the screenplay import product. Angular SSR, served in
dev by its own `ng serve` behind a per-client Envoy listener and in publish
mode by the unified SSR host (`clients/host/`).

Auth is the platform BFF: `/signin`, `/signup`, `/signout`, `/add-passkey`
and `/account/*` are auth-svc endpoints reached by full-page navigation, and
the pages themselves live in the `theplot` Keycloak realm's login theme
(`infra/keycloak/themes/theplot`). This app never holds a token — identity
arrives as the `x-user-id` / `x-tenant` headers Envoy's ext_authz injects
(see `src/app/auth/auth-identity.ts`).

The design system is Ember (`src/styles/ember.css`) — warm charcoal + amber,
a sibling of Protofast's Nocturne with the same class contract.

## Development

Run the whole stack from the AppHost (`aspire run` in `apphost/`); the
`theplot (web)` URL on the envoy resource is the entry point. Standalone:

```bash
npm start
```

`npm run build` runs proto codegen (`buf.gen.yaml` → `src/lib/gen/`) and then
`ng build`; `npm test` runs the unit tests.
