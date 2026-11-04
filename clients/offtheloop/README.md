# OffTheLoop client

The OffTheLoop web client: a job search's applications, the notes kept for its interviews and
the to-dos and career goals around them, from the "OffTheLoop Dashboard — Quiet" design
(claude.ai/artifact/13rd4ymCz5ZLJybzjzw9EJ). Angular SSR, served in dev by its own `ng serve`
behind ProtoStrap's local edge and deployed in the clients image, whose host (`clients/host/`)
serves it on `HOST_OFFTHELOOP`.

- `/` is the landing page; `/app` (Today), `/app/notes` (look up), `/app/loop` and `/app/account`
  need a session, which the SSR gate in `src/server.ts` checks.
- Sign-in is the platform BFF: `/signin`, `/signup`, `/signout`, `/add-passkey` and `/account/*`
  are auth endpoints reached by full-page navigation, and their pages are the `offtheloop` realm's
  Keycloak theme (`services/keycloak/themes/offtheloop`).
- Data is the `offtheloop` gRPC package on the api service (`Applications`, `Notes`, `Todos`,
  `Goals`), called over gRPC-Web on this origin and loaded in the browser after render.

The design tokens are in `src/styles/loop.css`; the Keycloak theme carries a copy.

## Development

Run the stack from the AppHost (`aspire start` in `apphost/`) and open
`http://offtheloop.dev.localhost:20000`. `npm run generate:grpc` writes the gRPC code
(`buf.gen.yaml` → `src/lib/gen/`), which is committed; `npm start` runs it first,
`npm run build` doesn't. `npm test` runs the unit tests.
