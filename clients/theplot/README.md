# ThePlot client

The ThePlot web client — landing page, sign-up/sign-in entry points, and
account management for the screenplay import product. Angular SSR, served in
dev by its own `ng serve` behind ProtoStrap's local edge and deployed in the
clients image, whose host (`clients/host/`) serves it on `HOST_THEPLOT`.

Auth is the platform BFF: `/signin`, `/signup`, `/signout`, `/add-passkey`
and `/account/*` are auth-svc endpoints reached by full-page navigation, and
the pages themselves live in the `theplot` Keycloak realm's login theme
(`services/keycloak/themes/theplot`). This app never holds a token — identity
arrives as the `x-user-id` / `x-tenant` headers the edge adds from auth's
answer to its authorization check (see `src/app/auth/auth-identity.ts`).

The design system is Ember (`src/styles/ember.css`) — warm charcoal + amber,
a sibling of Protofast's Nocturne with the same class contract.

## Development

Run the whole stack from the AppHost (`aspire run` in `apphost/`), and open
`http://theplot.dev.localhost:20000`. Standalone:

```bash
npm start
```

`npm run generate:grpc` writes the gRPC code (`buf.gen.yaml` → `src/lib/gen/`),
which is committed: the image is built from it, without the protos. `npm start`
runs it first; `npm run build` doesn't. `npm test` runs the unit tests.
