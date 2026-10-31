// ProtoFast's AppHost is the whole application, written once (docs/01-topology.md):
//
//   aspire run                       runs it here, behind the same edge ProtoStrap runs in an
//                                    account, at http://protofast.dev.localhost:20000 and the
//                                    theplot, admin and auth hosts beside it
//   aspire do protostrap-spec        writes ../protostrap.yaml, prod's spec
//
// Everything ProtoStrap-only (pools, requests, routes, scaling) is in the PublishAsProtoStrap*
// callbacks. Resources only for `aspire run` are added when run is true.

using System.Security.Cryptography;
using ProtoFast.AppHost;
using ProtoFast.AppHost.ClientApp;
using ProtoFast.AppHost.Postgres;

var builder = DistributedApplication.CreateBuilder(args);
var run = builder.ExecutionContext.IsRunMode;

// Each component reads only its connection strings, so the environment's secret holds one key
// per connection (docs/06-secrets.md).
const ReferenceEnvironmentInjectionFlags connectionStringOnly =
    ReferenceEnvironmentInjectionFlags.ConnectionString | ReferenceEnvironmentInjectionFlags.ServiceDiscovery;

// The local edge's port, and so every local origin's. Override it to run beside another AppHost
// that holds 20000: dotnet user-secrets set LocalEdge:Port 20100 (or --LocalEdge:Port).
var edgePort = int.TryParse(builder.Configuration["LocalEdge:Port"], out var port) ? port : 20000;
string LocalOrigin(string local) => $"http://{local}:{edgePort}";

// The spec's top. prod's Terraform root gives the four hosts their hostnames (infra/prod); the
// version matches the workflows' @v0.1.0 and the roots' ?ref=v0.1.0. The pools take the smallest
// VMs, t4g.small and t4g.medium: services in pairs across the general VMs, Spot VMs for the
// conversions and imports.
builder.AddProtoStrapEnvironment("protostrap", app: "protofast")
    .WithVersion("0.1.0")
    .WithHost("protofast", "protofast.dev", local: "protofast.dev.localhost")
    .WithHost("theplot", "theplot.protofast.dev", local: "theplot.dev.localhost")
    .WithHost("admin", "admin.protofast.dev", local: "admin.dev.localhost")
    .WithHost("auth", "auth.protofast.dev", local: "auth.dev.localhost")
    .WithLocalEdge(port: edgePort)
    .WithIngressPool(vm: "t4g.small")
    .WithGeneralPool(vm: ["t4g.medium"], min: 1, max: 4, spare: 1)
    .WithComputePool(vm: ["t4g.medium"], min: 0, max: 3)
    .WithDataPool(vm: "t4g.medium");

// Secrets that only exist locally are generated once and kept in the AppHost's user secrets.
// In an account an Org Admin sets them in the environment's secret (scripts/set-secrets.sh).
IResourceBuilder<ParameterResource> Generated(string name) =>
    builder.AddParameter(name, new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var keycloakAdminSecret = Generated("keycloak-admin-secret");
var protofastWebSecret = Generated("protofast-web-client-secret");
var theplotWebSecret = Generated("theplot-web-client-secret");
var adminClientSecret = Generated("admin-client-secret");
var accountAdminSecret = Generated("account-admin-client-secret");

// The internal JWT's key pair: auth signs with the private key, payments and the api verify
// with the public one. Locally it is made fresh each run, since tokens live five minutes.
using var internalJwtKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var internalJwtPrivateKey = builder.AddParameter("internal-jwt-private-key", internalJwtKey.ExportPkcs8PrivateKeyPem(), secret: true);
var internalJwtPublicKey = builder.AddParameter("internal-jwt-public-key", internalJwtKey.ExportSubjectPublicKeyInfoPem(), secret: true);
var internalJwtKeyId = builder.AddParameter("internal-jwt-key-id", "local", secret: true);

// The model APIs' keys, which document-import calls. Locally they are whatever the developer
// keeps in user secrets (Parameters:deepseek-api-key, ...); without them imports fail at the
// model call, and everything else still runs.
var deepseekApiKey = builder.AddLocalSecret("deepseek-api-key");
var geminiApiKey = builder.AddLocalSecret("gemini-api-key");
var anthropicApiKey = builder.AddLocalSecret("anthropic-api-key");

// Social sign-in's credentials, which keycloak-config writes into the realms. The providers ship
// disabled, so empty values (the default here, and in scripts/set-secrets.sh) change nothing.
string[] socialKeys = ["GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET", "APPLE_CLIENT_ID", "APPLE_TEAM_ID", "APPLE_KEY_ID", "APPLE_PRIVATE_KEY"];
var socialSecrets = socialKeys.ToDictionary(key => key, key => builder.AddLocalSecret(key.ToLowerInvariant().Replace('_', '-')));

// PostgreSQL holds Keycloak's, auth's and ThePlot's databases, each with a user of its own,
// which postgres-config makes from the connection strings. Redis is a cache: memory only. With
// ProtoStrap's telemetry (500m, 1536Mi) they fill data-1's 3 GiB after its reserve.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .PublishAsProtoStrapStateful(s => s
        .Vm("data-1")
        .Request(cpu: "1000m", memory: "1Gi")
        .Volume(size: "20Gi")
        .Stop(TimeSpan.FromSeconds(60)));
var keycloakDb = postgres.AddDatabase("keycloak-db", databaseName: "keycloak");
var authDb = postgres.AddDatabase("auth-db", databaseName: "auth");
var protofastDb = postgres.AddDatabase("protofast-db", databaseName: "protofast");

var postgresConfig = builder.AddProject<Projects.ProtoFast_PostgresConfig>("postgres-config")
    .WithReferenceEnvironment(connectionStringOnly)
    .WithReference(postgres, connectionName: "postgres")
    .WithReference(keycloakDb, connectionName: "keycloak")
    .WithReference(authDb, connectionName: "auth")
    .WithReference(protofastDb, connectionName: "protofast")
    .WithEnvironment("Databases__Names", "keycloak,auth,protofast")
    .PublishAsProtoStrapTask(t => t.Request(cpu: "100m", memory: "128Mi"));
postgres.WithProtoStrapConfigure(postgresConfig);

var redis = builder.AddRedis("redis")
    .PublishAsProtoStrapStateful(s => s
        .Vm("data-1")
        .Request(cpu: "250m", memory: "512Mi")
        // Sessions, sign-in correlation and rate limits, all with TTLs: bounded, evicted by TTL,
        // never written to disk. A restart is cache misses that auth repopulates.
        .Command("sh", "-c",
            "exec docker-entrypoint.sh redis-server --requirepass \"$(cat /run/secrets/REDIS_PASSWORD)\" " +
            "--maxmemory 256mb --maxmemory-policy volatile-ttl --save '' --appendonly no")
        .Volume(size: "1Gi"));

// Uploads go straight from browsers to the bucket, then through two queues to document-import.
var documents = builder.AddProtoStrapBucket("documents", browser: true, versions: TimeSpan.FromDays(7), locked: true);
var documentUpload = builder.AddProtoStrapQueue("document-upload", visibility: TimeSpan.FromMinutes(15));
// FIFO, grouped by document family, so each family's policy has one writer at a time.
var workflowOutcomes = builder.AddProtoStrapQueue("workflow-outcomes", fifo: true, visibility: TimeSpan.FromMinutes(15));
// auth and Keycloak send mail with SES, from no-reply@ the protofast host's domain.
var email = builder.AddProtoStrapEmail("email", "protofast.dev");

// Keycloak is built from its own Dockerfile: its providers (the email-code authenticator, the
// Apple identity provider, the SES email sender) and themes are in the image. Browsers reach
// only its sign-in pages, through the edge on the auth host; auth and keycloak-config reach it
// on the overlay. keycloak-config applies its realms once it is ready, and again on any change.
string[] realms = ["protofast", "theplot", "operators"];
var keycloakRoutes = realms
    .SelectMany(realm => new[]
    {
        $"auth/realms/{realm}/protocol/openid-connect/auth",
        $"auth/realms/{realm}/protocol/openid-connect/logout",
        $"auth/realms/{realm}/login-actions/",
        $"auth/realms/{realm}/broker/",
    })
    .Append("auth/resources/")
    .ToArray();
var keycloak = builder.AddDockerfile("keycloak", "../services/keycloak")
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WithReferenceEnvironment(connectionStringOnly)
    .WithReference(keycloakDb, connectionName: "keycloak").WaitFor(keycloakDb)
    .WithReference(email)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_ID", "protofast-admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_SECRET", keycloakAdminSecret)
    // No local OTLP export: the JVM doesn't trust the dashboard's development certificate. In an
    // account its entrypoint sends traces and logs to ProtoStrap's collector.
    .PublishAsProtoStrapService(s => s
        .Request(cpu: "500m", memory: "1Gi")
        .Ready(Readiness.Http("/health/ready").On(9000))
        .Routes(keycloakRoutes)
        .Stop(TimeSpan.FromSeconds(30))
        .SecretFiles());

// auth is the BFF: it signs people in, keeps their sessions in Redis, and answers the edge's
// question about every request to the clients, the api and payments (ExtAuthzEndpoint).
var authMigrations = builder.AddSchemaMigrations<Projects.ProtoFast_Auth_SchemaMigrations>("auth-migrations", authDb, connectionStringOnly)
    .PublishAsProtoStrapTask(t => t.Request(cpu: "100m", memory: "128Mi"));

// The browser's OIDC flow and account endpoints, on each client's host. A prefix list, never
// all of auth: /backchannel-logout, which deletes sessions on a logout token, stays unrouted.
string[] clientHosts = ["protofast", "theplot", "admin"];
var authRoutes = clientHosts
    .SelectMany(host => new[] { "/signin", "/signup", "/signout", "/reset", "/add-passkey", "/account/" }.Select(path => host + path))
    .ToArray();
var auth = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth")
    .WithReferenceEnvironment(connectionStringOnly)
    .WithReference(authDb, connectionName: "auth").WaitFor(authDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(email)
    .WithEnvironment("Keycloak__Authority", keycloak.GetEndpoint("http"))
    .WithEnvironment("Keycloak__ClientSecretProtofastWeb", protofastWebSecret)
    .WithEnvironment("Keycloak__ClientSecretTheplotWeb", theplotWebSecret)
    .WithEnvironment("Keycloak__ClientSecretAdmin", adminClientSecret)
    .WithEnvironment("Keycloak__AdminClientSecret", accountAdminSecret)
    .WithEnvironment("InternalJwt__PrivateKeyPem", internalJwtPrivateKey)
    .WithEnvironment("InternalJwt__KeyId", internalJwtKeyId)
    .WaitFor(keycloak)
    .WaitForCompletion(authMigrations)
    // On the http endpoint, as in an account: left to Aspire, the check would take the launch
    // profile's https one, which the local edge would then probe with plain HTTP.
    .WithHttpHealthCheck("/healthz", endpointName: "http")
    .PublishAsProtoStrapService(s => s
        .Request(cpu: "250m", memory: "256Mi")
        .Routes(authRoutes)
        .Stop(TimeSpan.FromSeconds(20))
        .Scale(min: 2, max: 4, demand => demand.Cpu(0.70)));

var keycloakConfig = builder.AddDockerfile("keycloak-config", "../services/keycloak-config")
    .WithEnvironment("KEYCLOAK_URL", keycloak.GetEndpoint("http"))
    // Where Keycloak posts logout tokens: auth on the overlay, never through the edge.
    .WithEnvironment("AUTH_URL", auth.GetEndpoint("http"))
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_ID", "protofast-admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_SECRET", keycloakAdminSecret)
    .WithEnvironment("Keycloak__ClientSecretProtofastWeb", protofastWebSecret)
    .WithEnvironment("Keycloak__ClientSecretTheplotWeb", theplotWebSecret)
    .WithEnvironment("Keycloak__ClientSecretAdmin", adminClientSecret)
    .WithEnvironment("Keycloak__AdminClientSecret", accountAdminSecret)
    .PublishAsProtoStrapTask(t => t
        .Request(cpu: "250m", memory: "512Mi")
        // It reads keycloak's own keys: the bootstrap admin client's secret is one value for both.
        .SecretPrefix("Keycloak_")
        .SecretFiles());
foreach (var (key, secret) in socialSecrets)
{
    keycloakConfig.WithEnvironment(key, secret);
}

keycloak.WithProtoStrapConfigure(keycloakConfig);

// The edge asks auth about each request to these services first, and passes on what it answers.
string[] identityHeaders = ["x-user-id", "x-tenant", "x-roles", "x-internal-jwt", "x-authenticated"];
ProtoStrapService Authorized(ProtoStrapService service) =>
    service.Authorize(auth, path: "/ext-authz", headers: identityHeaders, forward: ["cookie"], respond: ["set-cookie"]);

// payments and the api speak gRPC, which the edge translates from browsers' gRPC-Web. Browsers
// call /<package>.<Service>/<Method> on their own origin, so each service is routed by its
// protos' packages. They trust only the internal JWT that auth's answer carries.
string[] PackageRoutes(IEnumerable<string> hosts, params string[] packages) =>
    hosts.SelectMany(host => packages.Select(package => $"{host}/{package}.")).ToArray();

builder.AddProject<Projects.ProtoFast_Payments_Api>("payments")
    .WithEnvironment("InternalJwt__PublicKeyPem", internalJwtPublicKey)
    .PublishAsProtoStrapService(s => Authorized(s
        .Request(cpu: "100m", memory: "128Mi")
        .Ready(Readiness.Grpc)
        .Routes(PackageRoutes(clientHosts, "greet"))
        .Stop(TimeSpan.FromSeconds(15))));

var protofastMigrations = builder.AddSchemaMigrations<Projects.ProtoFast_SchemaMigrations>("protofast-migrations", protofastDb, connectionStringOnly)
    .PublishAsProtoStrapTask(t => t.Request(cpu: "100m", memory: "256Mi"));

builder.AddProject<Projects.ProtoFast_Api>("api")
    .WithReferenceEnvironment(connectionStringOnly)
    .WithReference(protofastDb, connectionName: "protofast").WaitFor(protofastDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(documents)
    .WithReference(documentUpload)
    .WithEnvironment("InternalJwt__PublicKeyPem", internalJwtPublicKey)
    .WithLocalStackCredentials()
    .WaitForCompletion(protofastMigrations)
    .PublishAsProtoStrapService(s => Authorized(s
        .Request(cpu: "250m", memory: "512Mi")
        .Ready(Readiness.Grpc)
        .Routes([
            .. PackageRoutes(clientHosts, "document", "document_upload", "story"),
            // The consoles' services, which check the operators realm and a console role again.
            .. PackageRoutes(["admin"], "admin"),
        ])
        .Stop(TimeSpan.FromSeconds(30))
        .Scale(min: 2, max: 4, demand => demand.Cpu(0.70))));

// conversion turns uploads into Markdown (Python: MarkItDown, Tesseract), and document-import
// drains the upload queue through it and the model APIs.
var conversion = builder.AddDockerfile("conversion", "../services/conversion")
    .WithHttpEndpoint(targetPort: 8090, name: "http")
    .WithHttpHealthCheck("/healthz")
    .WithReference(documents)
    .WithLocalStackCredentials()
    .WithLocalOtlpExporter()
    .PublishAsProtoStrapService(s => s
        .Pool(Pool.Compute)
        // Measured: one 40-page scanned PDF peaks at 494 MiB, two at once at 960 MiB, and
        // document-import runs two imports per replica. The memory request is a hard limit.
        .Request(cpu: "1000m", memory: "1536Mi")
        // The drain, the longest conversion (CONVERSION_TIMEOUT_SECONDS, 480 s) and its writes:
        // services/conversion/README.md.
        .Stop(TimeSpan.FromSeconds(620))
        .Scale(min: 1, max: 4, demand => demand.Cpu(0.70)));

builder.AddProject<Projects.ProtoFast_DocumentImport_Worker>("document-import")
    .WithReferenceEnvironment(connectionStringOnly)
    .WithReference(protofastDb, connectionName: "protofast").WaitFor(protofastDb)
    .WithReference(documents)
    .WithReference(documentUpload)
    .WithReference(workflowOutcomes)
    .WithEnvironment("Conversion__BaseUrl", conversion.GetEndpoint("http"))
    .WithEnvironment("Providers__deepseek__ApiKey", deepseekApiKey)
    .WithEnvironment("Providers__gemini__ApiKey", geminiApiKey)
    .WithEnvironment("Providers__anthropic__ApiKey", anthropicApiKey)
    .WithLocalStackCredentials()
    .WaitForCompletion(protofastMigrations)
    .PublishAsProtoStrapWorker(w => w
        .Pool(Pool.Compute)
        .Request(cpu: "500m", memory: "512Mi")
        // An import in progress is cancelled at stop, and its message comes back once its lease
        // lapses, so stop need only cover the drain.
        .Stop(TimeSpan.FromSeconds(45))
        // An import takes minutes of model calls: a replica per ten waiting, gone when none are.
        .Scale(min: 0, max: 4, demand => demand.Queue(documentUpload, rate: 1.0 / 60, drain: TimeSpan.FromMinutes(10))));

// Browsers send their traces and logs to the application's own collector, on their own origin,
// which passes them on to ProtoStrap's (ProtoStrap's telemetry, "Telemetry from browsers").
builder.AddDockerfile("otel-collector", "../services/otel-collector")
    .WithHttpEndpoint(targetPort: 4318, name: "http")
    .WithHttpEndpoint(targetPort: 13133, name: "health")
    .WithHttpHealthCheck("/", endpointName: "health")
    .WithLocalCollectorExport(builder.Configuration)
    .PublishAsProtoStrapService(s => s
        .Request(cpu: "100m", memory: "128Mi")
        .Ready(Readiness.Http("/").On(13133))
        .Route("protofast/otlp/", rate: 20)
        .Route("theplot/otlp/", rate: 20)
        .Route("admin/otlp/", rate: 20)
        .Stop(TimeSpan.FromSeconds(15)));

// The clients. In an account one Node host serves every client's SSR bundle, choosing by the
// host asked for, and each app console runs in a process of its own, reached only by holders of
// its role (docs/design/admin-consoles.md): auth's answer turns anyone else away. The roles are
// set here, never by a console's own code.
var platformConsole = new Dictionary<string, string> { ["ADMIN_CONSOLE_ROLES"] = "*" };
var theplotConsole = new Dictionary<string, string> { ["ADMIN_CONSOLE_ROLES"] = "admin-theplot" };
var clientsImage = !run || bool.TryParse(builder.Configuration["SsrHost:Dev"], out var ssrHost) && ssrHost;
if (clientsImage)
{
    // Set SsrHost__Dev=true (the https-ssr-host launch profile) to run this image under
    // `aspire run` instead of the dev servers: the same hosts, no HMR.
    IResourceBuilder<ContainerResource> ClientsHost(string name, string clients, string defaultClient, IReadOnlyDictionary<string, string> environment)
    {
        var host = builder.AddDockerfile(name, "../clients")
            .WithHttpEndpoint(targetPort: 4000, name: "http")
            .WithHttpHealthCheck("/healthz")
            // The host serves the CLIENTS' bundles, each for the host of its name (HOST_<NAME>).
            .WithEnvironment("CLIENTS", clients)
            .WithEnvironment("DEFAULT_CLIENT", defaultClient)
            .WithLocalOtlpExporter();
        foreach (var (key, value) in environment)
        {
            host.WithEnvironment(key, value);
        }

        return host;
    }

    ClientsHost("clients", "admin,protofast,theplot", "protofast", platformConsole)
        .PublishAsProtoStrapService(s => Authorized(s
            .Request(cpu: "250m", memory: "512Mi")
            .Routes([.. clientHosts.Select(host => host + "/")])
            .Stop(TimeSpan.FromSeconds(20))
            .Scale(min: 2, max: 4, demand => demand.Cpu(0.70))));
    ClientsHost("clients-admin-theplot", "admin-theplot", "admin-theplot", theplotConsole)
        .PublishAsProtoStrapService(s => Authorized(s
            .Request(cpu: "100m", memory: "256Mi")
            .Routes("admin/theplot/")
            .Stop(TimeSpan.FromSeconds(20))));
}
else
{
    // Each client's own Angular dev server, with HMR, behind the same routes and authorizer.
    void DevServer(string name, string local, string[] routes, IReadOnlyDictionary<string, string>? environment = null) =>
        builder.AddClientApp(name, LocalOrigin(local), environment)
            .PublishAsProtoStrapService(s => Authorized(s.Request(cpu: "250m", memory: "512Mi").Ready(Readiness.Tcp).Routes(routes)));

    DevServer("protofast", "protofast.dev.localhost", ["protofast/"]);
    DevServer("theplot", "theplot.dev.localhost", ["theplot/"]);
    DevServer("admin", "admin.dev.localhost", ["admin/"], platformConsole);
    DevServer("admin-theplot", "admin.dev.localhost", ["admin/theplot/"], theplotConsole);
}

if (run)
{
    // Mail goes to smtp4dev, at http://localhost:8025: auth sends with SMTP, and Keycloak with
    // its built-in sender rather than the image's SES one.
    var smtp4devPort = int.TryParse(builder.Configuration["Smtp4dev:Port"], out var smtpPort) ? smtpPort : 8025;
    var smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev", "latest")
        .WithHttpEndpoint(port: smtp4devPort, targetPort: 80, name: "web")
        .WithEndpoint(targetPort: 25, name: "smtp")
        .ExcludeFromManifest();
    var mailFromHost = smtp4dev.GetEndpoint("smtp", KnownNetworkIdentifiers.LocalhostNetwork);
    var mailFromContainers = smtp4dev.GetEndpoint("smtp", KnownNetworkIdentifiers.DefaultAspireContainerNetwork);
    auth.WaitFor(smtp4dev)
        .WithEnvironment("Smtp__Host", mailFromHost.Property(EndpointProperty.Host))
        .WithEnvironment("Smtp__Port", mailFromHost.Property(EndpointProperty.Port))
        .WithEnvironment("Smtp__StartTls", "false");

    // Keycloak runs in development mode, which builds itself as it starts, so its email sender
    // can be switched back to SMTP, and theme edits show on a refresh. The admin user is the one
    // a local database made before keycloak-config already has; keycloak-config logs in as it.
    var keycloakPassword = builder.AddParameter("keycloak-password", new GenerateParameterDefault { MinLength = 22, Special = false }, secret: true, persist: true);
    keycloak.WithArgs("start-dev")
        .WithEnvironment("KC_SPI_EMAIL_SENDER__PROVIDER", "default")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakPassword)
        .WithBindMount("../services/keycloak/themes", "/opt/keycloak/themes", isReadOnly: true);
    keycloakConfig
        .WithEnvironment("KEYCLOAK_GRANTTYPE", "password")
        .WithEnvironment("KEYCLOAK_USER", "admin")
        .WithEnvironment("KEYCLOAK_PASSWORD", keycloakPassword)
        .WithEnvironment("SMTP_HOST", mailFromContainers.Property(EndpointProperty.Host))
        .WithEnvironment("SMTP_PORT", mailFromContainers.Property(EndpointProperty.Port))
        // The admin consoles' operators: sign in with the email, and the code arrives in smtp4dev.
        .WithEnvironment("DEV_OPERATORS", "protofast@admin.test=platform,admin-protofast;theplot@admin.test=admin-theplot");

    postgres.WithPgAdmin();
}

builder.Build().Run();
