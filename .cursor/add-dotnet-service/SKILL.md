---
name: add-dotnet-service
description: >-
  Adds a .NET gRPC service to ProtoFast: its project under services/, its one registration in
  apphost/Program.cs with ProtoStrap.Hosting, routes by proto package, tests, the spec and the
  docs. Use when asked to add a backend, API or gRPC service written in .NET.
disable-model-invocation: true
---

# Add a .NET gRPC service

ProtoFast's AppHost describes every component once: `aspire run` runs it behind ProtoStrap's
local edge, and `aspire do protostrap-spec` writes `protostrap.yaml` from it. payments is the
smallest complete service; the api adds a database, Redis, a bucket and a queue. ProtoStrap's
general rules are in its skills: protostrap-aspire (AppHost calls and what they become),
protostrap-contract (readiness, drain, keys), protostrap-secrets, protostrap-telemetry,
protostrap-email, protostrap-deploy.

## Names

- `<name>`: the component, lowercase with dashes (`billing`). Its folder, `<folder>`, has
  underscores for dashes, as `services/document_import` does.
- `<Name>`: PascalCase (`Billing`). The project and its namespace are `ProtoFast.<Name>.Api`.
- `<package>`: the proto package it serves. The edge routes browsers' calls by it.

## Decide first

Ask when the request doesn't say. Each is the developer's call:

- **Who calls it:** browsers through the edge, authorized by auth (as payments), or only
  other components on the overlay (no routes).
- **Which hosts route to it:** every client host (`clientHosts`) or some, as the api's
  `admin.` package is on `["admin"]` only.
- **Pool:** general, the default, or `Pool.Compute` (Spot VMs) for CPU-heavy or batch work.
- **Scale:** one replica (no `Scale`, as payments) or `Scale(min: 2, ...)` to survive a VM's loss.
- **Database:** none, the `protofast` database (as document-import), or one of its own.
- **Keys:** any secret it reads besides the shared ones.

## Steps

1. **Scaffold.**
   `dotnet new grpc -n ProtoFast.<Name>.Api -o services/<folder>/src/ProtoFast.<Name>.Api`.
   In the `.csproj`, as payments': `<RootNamespace>ProtoFast.<Name>.Api</RootNamespace>`, the
   `Grpc.AspNetCore` version of the api's `.csproj`, and a `ProjectReference` to
   `..\..\..\shared\ServiceDefaults\ServiceDefaults.csproj`, which brings `shared\ProtoStrap`.
   From the same `shared` folder add `Grpc\Grpc.csproj` for `CallerIdentity`, and
   `Storage\Storage.csproj` with `Storage.Abstractions\Storage.Abstractions.csproj` for S3 or SQS.

2. **Its own proto package.** In `Protos/`, `package <package>;` and
   `option csharp_namespace = "ProtoFast.<Name>";`; the template's `greet` is payments'.
   Browsers call `/<package>.<Service>/<Method>`, so pick one no route in `protostrap.yaml`
   covers. Add `ProtoRoot="Protos"` to the `<Protobuf>` items when protos import each other.

3. **HTTP/2 on every address.** Keep the template's `Kestrel:EndpointDefaults:Protocols: Http2`
   in `appsettings.json`: the edge speaks cleartext HTTP/2 to a gRPC service. Set the `http`
   profile's `applicationUrl` in `Properties/launchSettings.json` to `http://0.0.0.0:0`, as
   payments': the local edge, a container, reaches projects at `host.docker.internal`.

4. **The service's `Program.cs`,** as payments':

   ```csharp
   builder.AddServiceDefaults(stop: TimeSpan.FromSeconds(13)); // Inside the spec's stop.
   builder.Services.AddInternalJwtAuth(builder.Configuration);
   builder.Services.AddGrpc(options => options.Interceptors.Add<InternalJwtAuthInterceptor>());
   var app = builder.Build();
   app.MapDefaultEndpoints(grpc: true);
   app.MapGrpcService<...>();
   ```

   - `AddServiceDefaults` reads the component's keys from the environment's secret
     (`SECRET_ID`, `SECRET_PREFIX`), exports telemetry from the `OTEL_` variables, and adds
     readiness and the drain. `stop` covers the 10 s drain and the longest call, a couple of
     seconds inside the spec's `Stop` (13 in 15, 28 in 30).
   - `MapDefaultEndpoints(grpc: true)` maps `GET /healthz` and gRPC's health service, which
     answers for the component's name: the spec's `ready: grpc`.
   - The interceptor rejects calls without auth's internal JWT; `CallerIdentity.From(context)`
     is the caller. With a database, `UserContextInterceptor` follows it, as in the api.
   - S3 and SQS: `AddS3ObjectStorage(..., bucket: "<bucket>")` and
     `AddSqsQueue(<key>, ..., queue: "<queue>")` read `BUCKET_<NAME>` and `QUEUE_<NAME>_URL`.
   - Mail: auth's pattern, SES v2 when `Replica.UnderProtoStrap`, SMTP to smtp4dev otherwise
     (`services/auth/src/ProtoFast.Auth.Api/Program.cs`, `Email/`); in the AppHost,
     `.WithReference(email)` and the `Smtp__*` variables of the `if (run)` block, as auth has.

5. **Solution and AppHost reference.**
   `dotnet sln ProtoFast.slnx add services/<folder>/src/ProtoFast.<Name>.Api/ProtoFast.<Name>.Api.csproj`
   files it under `/services/<folder>/src/` like the others. Add its `ProjectReference` to
   `apphost/ProtoFast.AppHost.csproj` beside the services'.

6. **Register it in `apphost/Program.cs`** after payments, whose `Authorized` and
   `PackageRoutes` it uses:

   ```csharp
   builder.AddProject<Projects.ProtoFast_<Name>_Api>("<name>")
       .PublishAsProtoStrapService(s => Authorized(s
           .Request(cpu: "100m", memory: "128Mi")
           .Ready(Readiness.Grpc)
           .Routes(PackageRoutes(clientHosts, "<package>"))
           .Stop(TimeSpan.FromSeconds(15))));
   ```

   - `Ready(Readiness.Grpc)` makes it a `grpc` service: the edge speaks gRPC to it and
     translates gRPC-Web. `PackageRoutes` routes `<host>/<package>.` on each host, and
     `Authorized` has the edge ask auth first and pass on `x-internal-jwt` and four more headers.
     It verifies the JWT with its own copy of the public key, `<Name>_InternalJwt__PublicKeyPem`
     (step 8).
   - `Request` is required. Add `.Pool(Pool.Compute)` and
     `.Scale(min: 2, max: 4, demand => demand.Cpu(0.70))` as decided.
   - Overlay only: no `Routes`, no `Authorized`; give each caller
     `WithEnvironment("<Name>__BaseUrl", <name>.GetEndpoint("http"))`, as document-import gets
     conversion's. No gRPC service is called that way yet: agree with the user how it
     authenticates callers.
   - A bucket or queue: `.WithReference(...)`; a new one is `AddProtoStrapBucket` or
     `AddProtoStrapQueue`. A process signs its LocalStack calls with `AWS_PROFILE`: never give it
     `.WithLocalStackCredentials()`.
   - A key of its own needs nothing here: it reads `<Name>_Section__Key` from the secret,
     `protofast/dev` locally. A key several components read is a copy under each one's prefix,
     not `Shared_` (docs/06-secrets.md, "Adding a secret").

7. **A database of its own,** if decided:
   - `var <name>Db = postgres.AddDatabase("<name>-db", databaseName: "<db>");`; on
     postgres-config, `.WithReference(<name>Db, connectionName: "<db>")` and `<db>` in
     `Databases__Names`.
   - `ProtoFast.<Name>.SchemaMigrations`, like `services/api/src/ProtoFast.SchemaMigrations`
     with its own advisory lock id, in the solution and the AppHost's references as in step 5,
     added with
     `builder.AddSchemaMigrations<Projects.ProtoFast_<Name>_SchemaMigrations>("<name>-migrations", <name>Db, connectionStringOnly)`
     and `.PublishAsProtoStrapTask(t => t.Request(cpu: "100m", memory: "128Mi"))`.
   - On the service: `.WithReferenceEnvironment(connectionStringOnly)`,
     `.WithReference(<name>Db, connectionName: "<db>").WaitFor(<name>Db)` and
     `.WaitForCompletion(<name>Migrations)`, the spec's `migrate`; in its code,
     `builder.AddNpgsqlDataSource("<db>")`, as the api's.
   - `<db>` in `scripts/set-secrets.sh`'s `for db in keycloak auth protofast` loop, which makes
     `Shared_ConnectionStrings__<db>` and a `<db>-db` rotate group; name it in the header.

8. **Prod's keys.** A key that can be generated goes in `scripts/set-secrets.sh`; an Org Admin
   sets any other with ProtoStrap's `scripts/set-secret.sh protofast/prod <Name>_Section__Key=-`,
   before the release that reads it (docs/06-secrets.md, "Adding a secret"). For the internal
   JWT, add `<Name>_InternalJwt__PublicKeyPem` to set-secrets.sh's internal JWT block, its `has`
   and its `put`, beside the api's and payments'.

9. **Tests,** written before the code: `services/<folder>/tests/ProtoFast.<Name>.UnitTests`,
   with the xunit.v3 packages of
   `services/postgres_config/tests/ProtoFast.PostgresConfig.UnitTests`'s `.csproj`, or
   `.IntegrationTests` with Testcontainers for PostgreSQL, as the api's. Add each to
   `ProtoFast.slnx`: CI runs `dotnet test ProtoFast.slnx`.

10. **Clients that call it:** its `Protos` directory in each one's `buf.gen.yaml`, then
    `npm run generate:grpc` and commit `src/lib/gen` (add-angular-client's
    `references/proto-codegen.md`). The transport doesn't change.

11. **The spec.** `aspire do protostrap-spec`; commit `protostrap.yaml` with the change, never
    edited by hand. Read its diff: new `routes`, `uses` (permissions), `secrets`, `migrate`.

12. **CI** needs no entry for a .NET project. ci.yml's `dotnet` job tests the solution, checks
    the spec is what the AppHost writes and runs `protostrap check`; ProtoStrap's deploy builds
    the image from the project on the base `Directory.Build.targets` pins. A service with a
    Dockerfile of its own goes in the `images` matrix instead.

13. **Docs,** where its peers are listed: docs/01-topology.md (components, the hosts'
    "Serves"), docs/02-local-development.md (what the AppHost starts),
    docs/03-services-and-clients.md (the .NET services, the internal JWT's readers,
    `Protocols: Http2`), docs/04-edge.md (routes, authorized services), docs/06-secrets.md
    (its keys, the public key's copies), docs/07-deployment.md (the .NET
    images), docs/09-reference.md (its variables, the file map, port 8080) and README.md.

## Check it works

- `aspire run`: the service is Running on the dashboard, with its logs and traces.
- An anonymous call through the edge reaches the service, and its JWT check refuses it:

  ```bash
  printf '\0\0\0\0\0' | curl -si --data-binary @- \
    -H 'content-type: application/grpc-web+proto' -H 'x-grpc-web: 1' \
    http://protofast.dev.localhost:20000/<package>.<Service>/<Method> | grep -ai grpc-status
  ```

  `grpc-status: 16` (Unauthenticated) is the service answering; a 404 is no route. Signed in,
  a call from a client page is one trace on the dashboard, from the browser's span to the
  service's.
- The `protostrap-edge` console logs each request and the service it went to: `NR` is no
  route, `UH` no ready replica.
- `dotnet test ProtoFast.slnx`.
- After committing the spec, `aspire do protostrap-spec` leaves `protostrap.yaml` unchanged;
  CI fails one that differs. With `protostrap` on `PATH` the step runs `protostrap check` too.
