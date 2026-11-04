# Protofast client

The protofast product site: landing page, sign-in entry point, account management and
subscriptions. Angular SSR, served in dev by its own `ng serve` behind ProtoStrap's local edge
and deployed in the clients image, whose host (`clients/host/`) serves it on `HOST_PROTOFAST`.

Auth is the platform BFF: `/signin`, `/signout`, `/add-passkey` and `/account/*` are auth
endpoints reached by full-page navigation, and the pages themselves live in the
`protofast` Keycloak realm's login theme (`services/keycloak/themes/protofast`). This app never
holds a token: identity arrives as the `x-user-id` / `x-tenant` headers the edge adds from
auth's answer to its authorization check (see `src/app/auth/auth-identity.ts`). gRPC-Web calls
go to the page's own origin, by proto package (`src/app/grpc-transport.ts`).

Sign-up is closed: Protofast is invite-only, so the `protofast` realm has registration off and
the landing page's sign-up buttons open an invite-only notice instead of `/signup`.

The design system is Nocturne (`src/styles/nocturne.css`).

## Development

Run the whole stack from the repository root (`aspire run`), and open
`http://protofast.dev.localhost:20000`. Standalone, for UI work only (nothing routes its API
calls or signs anyone in):

```bash
npm start
```

`npm run generate:grpc` writes the gRPC code (`buf.gen.yaml` → `src/lib/gen/`), which is
committed: the image is built from it, without the protos. `npm start` runs it first;
`npm run build` doesn't. `npm test` runs the unit tests.
