# Admin client

The platform admin console, on the admin host. Angular SSR, built on the admin kit
(`src/admin-kit`, a link to `../admin-kit/src`; see `clients/admin-kit/README.md`), served in
dev by its own `ng serve` behind ProtoStrap's local edge and deployed in the clients image,
whose host (`clients/host/`) serves it on `HOST_ADMIN`.

Operators sign in to the `operators` realm through the platform BFF, as on every host. This
app never holds a token: identity and roles arrive as the headers the edge adds from auth's
answer to its authorization check. It admits the roles in `ADMIN_CONSOLE_ROLES` (`*` here),
and links to the app consoles mounted on the same host (`src/app/consoles.ts`). Those are
apps of their own: ThePlot's is `clients/admin-theplot`, which the edge routes `/theplot/` to
(docs/design/admin-consoles.md).

## Development

Run the whole stack from the repository root (`aspire run`), and open
`http://admin.dev.localhost:20000`; sign in as `protofast@admin.test` with the code smtp4dev
receives. Standalone, without the edge, every page sends you to `/signin`, so it is for build
checks only:

```bash
npm start
```

`npm run generate:grpc` writes the gRPC code (`buf.gen.yaml` → `src/lib/gen/`), which is
committed: the image is built from it, without the protos. `npm test` runs the unit tests, and
`python3 scripts/check-admin-console.py clients/admin`, from the repository root, the console
isolation checks CI runs.
