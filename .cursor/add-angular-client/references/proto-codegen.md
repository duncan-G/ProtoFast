# gRPC code and transport for a client

Each client has its own `buf.gen.yaml`, naming the protos it calls. `protoc-gen-es` writes
TypeScript to `src/lib/gen`, which is committed: the clients image is built from `clients/`
alone, without `services/`. `<name>` is the client.

## 1. Dependencies

Copied from theplot's `package.json` in the skill's step 2: `@bufbuild/protobuf`,
`@connectrpc/connect` and `@connectrpc/connect-web` at run time, `@bufbuild/buf` and
`@bufbuild/protoc-gen-es` as dev dependencies.

## 2. `clients/<name>/buf.gen.yaml`

As theplot's, with an input per service whose protos the client calls:

```yaml
version: v2
inputs:
  - directory: ../../services/payments/src/ProtoFast.Payments.Api/Protos
  - directory: ../../services/api/src/ProtoFast.Api/Protos
    # Admin RPCs belong to the admin consoles only (docs/design/admin-consoles.md §3).
    exclude_paths:
      - ../../services/api/src/ProtoFast.Api/Protos/Admin
plugins:
  - local: protoc-gen-es
    out: src/lib/gen
    opt: target=ts
clean: true
```

- Keep `Protos/Admin` out of any client that isn't an admin console.
- auth is never an input: browsers reach it over plain HTTP.
- `paths:` under an input narrows it to some files, as `clients/admin/buf.gen.yaml` does.

## 3. Generate and commit

```bash
cd clients/<name> && npm run generate:grpc
```

Commit `src/lib/gen`. `npm start` runs the generation first; `npm run build` doesn't. CI runs
it again and fails when the result differs from what is committed, so run it after every
change to a proto the client reads, in each client that reads it.

## 4. The transport

`src/app/grpc-transport.ts`, copied from theplot, provides `GRPC_TRANSPORT`:
`createGrpcWebTransport({ baseUrl: inject(SERVER_URL), interceptors })`.

- The base URL has no path. Browsers call `/<package>.<Service>/<Method>` on the page's own
  origin, and the edge routes each package to the service that serves it
  (`PackageRoutes` in `apphost/Program.cs`). There is no CORS and no prefix to strip.
- `SERVER_URL` is the AppHost's `http://<name>.dev.localhost:20000` under `ng serve`, handed
  to the browser through `TransferState`; in the clients image it is unset and the browser
  uses its own origin.
- `traceInterceptor` (`src/lib/grpc-trace.interceptor.ts`) makes a span for each call, and
  the browser's fetch instrumentation sends its `traceparent`. In the browser, a call answered
  `Unauthenticated` sends the page to `/signin`.

A service client is `createClient(<Service>, inject(GRPC_TRANSPORT))`, with `<Service>` from
`src/lib/gen/<file>_pb`, as in theplot's `src/app/stories/connect-story-api.ts`.

## 5. The host's routes

A client calls only what its host routes. A host in `clientHosts` gets the api's and
payments' packages. A new service's package is routed by that service's own registration
(the add-dotnet-service skill).
