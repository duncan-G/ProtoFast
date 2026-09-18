using ProtoFast.AppHost.Aws;
using ProtoFast.AppHost.ClientApp;
using ProtoFast.AppHost.Conversion;
using ProtoFast.AppHost.EnvoyProxy;
using ProtoFast.AppHost.LocalStack;
using ProtoFast.AppHost.OpenTelemetryCollector;
using ProtoFast.AppHost.Postgres;

var builder = DistributedApplication.CreateBuilder(args);

if (!builder.ExecutionContext.IsPublishMode)
{
    AwsDeveloperSso.EnsureAuthenticated();
}

// The unified SSR host serves every client in publish mode. Set SsrHost__Dev=true
// (or run with --SsrHost:Dev=true) to smoke-test it locally instead of per-client
// dev servers — same Envoy listener URLs, no HMR.
var useSsrHost = builder.ExecutionContext.IsPublishMode
    || bool.TryParse(builder.Configuration["SsrHost:Dev"], out var ssrHostDev) && ssrHostDev;

// LocalStack signs requests against a region the way real AWS does, and the SDK's credential
// chain finds none in development — so one value is injected into LocalStack's init script and
// into both consumers rather than left to each of them to guess at.
//
// It follows the developer's SSO region because that is the one region already in play locally:
// WithSsoProfile puts it in AWS_REGION for Secrets Manager, and a LocalStack call signed for a
// different region looks for queues in a region nothing ever created any in — QueueDoesNotExist
// against a queue the init script plainly made. Matching them makes that impossible, and it is
// also the region the workload actually runs in (infra/backend.tf). Publish mode never resolves
// an SSO region and never runs LocalStack, so it falls back to the same default.
var awsRegion = string.IsNullOrWhiteSpace(AwsDeveloperSso.Region) ? "us-west-2" : AwsDeveloperSso.Region;

var otel = builder.AddOpenTelemetryCollector("otel-collector");

var postgres = builder
    .AddPostgres("postgres");

if (!builder.ExecutionContext.IsPublishMode)
{
    postgres
        .WithPgAdmin()
        .WithDataVolume();
}

postgres.AddDatabase("keycloak-db", databaseName: "keycloak");

var authDb = postgres
    .AddDatabase("auth-db", databaseName: "auth")
    .WithSchemaMigrations<Projects.ProtoFast_Auth_SchemaMigrations>(builder);

// Segmentation's own database on the same instance, created the same way auth's is (plan §8.4).
var segmentationDb = postgres
    .AddDatabase("segmentation-db", databaseName: "segmentation")
    .WithSchemaMigrations<Projects.ProtoFast_Segmentation_SchemaMigrations>(builder);

var redis = builder.AddRedis("redis");

// S3 and SQS for the segmentation feature. `aspire run` has to start the whole thing, or the
// feature will only ever be tested in prod (plan §22).
const string segmentationBucket = "protofast-segmentation-dev";
var localstack = builder.AddLocalStack("localstack", segmentationBucket, awsRegion);

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithImageTag("26.7")
    .WithRealmImport("../infra/keycloak/realms")
    // Enable ${env.VAR:default} placeholder substitution in the realm import files.
    // Without this, Keycloak imports the literal defaults (e.g. SMTP host localhost:1025)
    // instead of the SMTP_* env vars we inject below.
    .WithEnvironment("JAVA_OPTS_APPEND", "-Dkeycloak.migration.replace-placeholders=true")
    // Custom "protofast" login theme (referenced by loginTheme in the realm import).
    // start-dev disables theme caching, so edits under this dir show up on refresh.
    .WithBindMount("../infra/keycloak/themes", "/opt/keycloak/themes", isReadOnly: true)
    // Server extensions: the email-OTP authenticator and required action the browser
    // flow depends on, plus the Apple identity provider. The built JAR is staged under
    // deploy/ (not infra/) so dev and prod load the same artifact — rebuild it with
    // infra/keycloak/providers/build.sh after changing the Java, and restart Keycloak.
    .WithBindMount("../deploy/keycloak/providers", "/opt/keycloak/providers", isReadOnly: true)
    // Ship Keycloak's own logs to the collector's OTLP logs pipeline (same collector
    // as traces/metrics). opentelemetry-logs is a Preview feature, so it must be
    // listed in KC_FEATURES before the telemetry-logs options are recognized.
    .WithReference(otel)
    .WithEnvironment("KC_FEATURES", "opentelemetry-logs")
    .WithEnvironment("KC_TELEMETRY_LOGS_ENABLED", "true")
    .WithEnvironment("KC_TELEMETRY_LOGS_PROTOCOL", "grpc")
    .WithEnvironment(
        "KC_TELEMETRY_LOGS_ENDPOINT",
        otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName))
    // Emit Keycloak server spans to the same collector as the .NET services. Tracing is a
    // supported (non-preview) feature in Keycloak 26, so it needs no KC_FEATURES entry.
    // parentbased_always_on keeps the back-channel token/JWKS calls (which arrive with a
    // traceparent from the auth service) on the same trace as the sign-in flow.
    .WithEnvironment("KC_TRACING_ENABLED", "true")
    .WithEnvironment("KC_TRACING_PROTOCOL", "grpc")
    .WithEnvironment("KC_TRACING_SAMPLER_TYPE", "parentbased_always_on")
    .WithEnvironment(
        "KC_TRACING_ENDPOINT",
        otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpGrpcEndpointName))
    // Suppress embedded-Infinispan cache spans (named after the cache, e.g.
    // OFFLINE_USER_SESSION / OFFLINE_CLIENT_SESSION). These come from background
    // session-persistence tasks not tied to an incoming request, so they're just
    // noise. Keeps the request-scoped auth/sign-in spans intact.
    .WithEnvironment("KC_TRACING_INFINISPAN_ENABLED", "false");

IResourceBuilder<ContainerResource>? smtp4dev = null;
if (!builder.ExecutionContext.IsPublishMode)
{
    // Pin only the web UI host port so the inbox stays bookmarkable. SMTP can
    // float — Keycloak and auth both take the allocated mapping from the
    // endpoint references below, and nothing a person types needs it.
    smtp4dev = builder.AddContainer("smtp4dev", "rnwood/smtp4dev")
        .WithHttpEndpoint(port: 5000, targetPort: 80, name: "web")
        .WithEndpoint(targetPort: 25, name: "smtp");

    // Keycloak is a container, so it has to reach smtp4dev by container DNS and the
    // in-network SMTP port — not the host-published mapping auth-svc uses below.
    var mailFromContainer = smtp4dev.GetEndpoint(
        "smtp", KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

    keycloak
        .WithEnvironment("SMTP_HOST", mailFromContainer.Property(EndpointProperty.Host))
        .WithEnvironment("SMTP_PORT", mailFromContainer.Property(EndpointProperty.Port))
        .WithEnvironment("SMTP_FROM", "no-reply@protofast.dev")
        // WebAuthn RP ID must be the origin hostname. protofast.dev is correct in
        // prod (covers auth.protofast.dev); locally the ceremony runs on localhost.
        .WithEnvironment("WEBAUTHN_RP_ID", "localhost")
        // ThePlot's realm keeps its own RP ID so its passkeys stay scoped to it: sharing
        // protofast.dev would have the authenticator offer credentials the theplot realm
        // has no record of. Locally both realms land on the same localhost origin.
        .WithEnvironment("THEPLOT_WEBAUTHN_RP_ID", "localhost");
}

// Auth
var auth = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth")
    .WithOtlpCollectorReference(otel)
    .WithReference(redis)
    .WithReference(authDb, connectionName: "auth")
    .WaitFor(redis)
    .WaitFor(authDb)
    .WaitFor(keycloak)
    .WithEnvironment("Auth_Keycloak__Authority", keycloak.GetEndpoint("http"))
    .WithSsoProfile();

if (smtp4dev is not null)
{
    // Auth is a host process. appsettings.Development.json still says localhost:1025
    // (MailHog's usual mapping), but Aspire publishes smtp4dev's SMTP on an allocated
    // port — so without these, email-change hits connection-refused on 1025.
    var mailFromHost = smtp4dev.GetEndpoint("smtp", KnownNetworkIdentifiers.LocalhostNetwork);
    auth
        .WaitFor(smtp4dev)
        .WithEnvironment("Auth_Smtp__Host", mailFromHost.Property(EndpointProperty.Host))
        .WithEnvironment("Auth_Smtp__Port", mailFromHost.Property(EndpointProperty.Port))
        .WithEnvironment("Auth_Smtp__StartTls", "false")
        .WithEnvironment("Auth_Smtp__From", "no-reply@protofast.dev");
}

// Where Keycloak POSTs the logout token when an SSO session ends. Keycloak runs in a container
// and auth is a host process, so this needs the host-gateway alias the same way Envoy's upstream
// clusters do — endpoint.Property resolves to host.docker.internal from inside the container.
//
// This reads auth's endpoint but adds no wait edge (auth already waits on keycloak, and the
// reverse would deadlock): Aspire allocates every endpoint before it starts anything.
//
// Both apps share one cookie jar in dev — the browser ignores ports, so localhost:20000 and
// :20001 are one host with one pf_session — which means there is only ever one session here and
// nothing for a back-channel logout to reach across to. It is wired anyway so a broken URL or
// payload surfaces locally instead of in prod; the coverage lives in the test suite.
var authHttp = auth.GetEndpoint("http");
keycloak
    .WithContainerRuntimeArgs("--add-host=host.docker.internal:host-gateway")
    .WithEnvironment(
        "BACKCHANNEL_LOGOUT_URL",
        ReferenceExpression.Create(
            $"http://{authHttp.Property(EndpointProperty.Host)}:{authHttp.Property(EndpointProperty.Port)}/backchannel-logout"));

// Payments
var payments = builder.AddProject<Projects.ProtoFast_Payments_Api>("payments")
    .WithOtlpCollectorReference(otel)
    .WithSsoProfile();

// Api — Greeter plus the Segmentation surface (plan §17). It presigns, enqueues and reads
// Postgres; everything model-shaped happens in the worker.
var api = builder.AddProject<Projects.ProtoFast_Api>("api")
    .WithOtlpCollectorReference(otel)
    .WithReference(segmentationDb, connectionName: "segmentation")
    .WaitFor(segmentationDb)
    .WaitFor(localstack)
    .WithEnvironment("Api_Segmentation__ServiceUrl", LocalStackResourceBuilderExtensions.GatewayUrl)
    .WithEnvironment("Api_Segmentation__Region", awsRegion)
    .WithEnvironment("Api_Segmentation__Bucket", segmentationBucket)
    .WithEnvironment("Api_Segmentation__Runs", localstack.QueueUrl("protofast-segmentation-runs"))
    .WithEnvironment("Api_Segmentation__Bulk", localstack.QueueUrl("protofast-segmentation-runs-bulk"))
    .WithEnvironment("Api_Segmentation__BatchPoll", localstack.QueueUrl("protofast-segmentation-batch-poll"))
    .WithSsoProfile();

// The document-conversion sidecar (ingest plan §19). It reads the uploaded source from S3 and
// writes the Markdown, the layout and the report back to S3 itself, so the bytes never cross the
// wire between it and the worker. It owns no database, no queue and no state.
var conversion = builder
    .AddConversionService("conversion", segmentationBucket, awsRegion)
    .WithLocalStack(localstack);

// Segmentation worker. It publishes no port — nothing dials it; it pulls from SQS and writes to
// S3 and Postgres (plan §7.1). Provider keys are Seg_ entries in protofast/dev, read in-process
// the same way auth reads Keycloak secrets. A developer with no keys still gets a working stack:
// every phase up to labelling is deterministic, and the pipeline short-circuits labelling for
// clean Markdown (plan §9.4), so clean fixtures run end to end with no provider at all.
var segmentation = builder.AddProject<Projects.ProtoFast_Segmentation_Worker>("segmentation")
    .WithOtlpCollectorReference(otel)
    .WithReference(redis)
    .WithReference(segmentationDb, connectionName: "segmentation")
    .WaitFor(redis)
    .WaitFor(segmentationDb)
    .WaitFor(localstack)
    // The worker fails phase 0 for any non-Markdown upload while the converter is down, so it
    // waits for it rather than starting into a window where PDFs would fail for no visible reason.
    .WaitFor(conversion)
    .WithEnvironment("Seg_Conversion__Endpoint", conversion.GetEndpoint("http"))
    .WithEnvironment("Seg_Storage__ServiceUrl", LocalStackResourceBuilderExtensions.GatewayUrl)
    .WithEnvironment("Seg_Storage__Region", awsRegion)
    .WithEnvironment("Seg_Storage__Bucket", segmentationBucket)
    .WithEnvironment("Seg_Queues__Runs", localstack.QueueUrl("protofast-segmentation-runs"))
    .WithEnvironment("Seg_Queues__Bulk", localstack.QueueUrl("protofast-segmentation-runs-bulk"))
    .WithEnvironment("Seg_Queues__BatchPoll", localstack.QueueUrl("protofast-segmentation-batch-poll"))
    .WithSsoProfile();

// Prompts and completions as attributes on the gen_ai.* spans, so a run can be read in the
// dashboard as the conversation it actually was rather than as token counts. Run mode only: the
// variable is the OpenTelemetry semantic-convention switch that Microsoft.Extensions.AI reads
// directly, and the messages it attaches are document text — which is exactly what the routing
// layer's sensitivity rules exist to keep out of places it was not approved for (plan §14.1).
if (!builder.ExecutionContext.IsPublishMode)
{
    segmentation.WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true");
}

// Envoy Proxy
var proxy = builder.AddEnvoyProxy("envoy", useSsrHost)
    .WithOtelCollectorEndpoints(otel)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

// The worker publishes no port and has no Envoy route, so it is not wired into the proxy at all —
// it is reached by nothing and pulls from SQS instead (plan §7.1). Aspire starts it because it was
// added to the builder above; the discard just makes that explicit to a reader of this file.
_ = segmentation;

var otelHttp = otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName);

// Clients: each gets its own Envoy listener (dev) or domain virtual host (publish).
var adminWeb = proxy.WithClient(builder, "admin");
var protofastWeb = proxy.WithClient(builder, "protofast");
// Third registration, so the listener port follows automatically: 20002 (plan §18.2).
var theplotWeb = proxy.WithClient(builder, "theplot");

// The presigned PUT is the one call the browser makes to something other than Envoy, so
// LocalStack has to recognise the listener origins the pages are served from. Placed after the
// WithClient calls above because that is where the listener list becomes complete.
localstack.WithClientOrigins(proxy.GetClientOrigins());

if (useSsrHost)
{
    var clientsHost = builder.AddClientHost(
        "clients", defaultClient: "admin", proxy.GetClientHostnames(), otelHttp, otelHttp);
    proxy
        .WithUpstreamEndpoint("CLIENTS_HOST", clientsHost)
        .WithEnvironment("DEFAULT_CLIENT", "admin");
}
else
{
    var adminDev = builder.AddClientApp("admin", "../clients/admin", adminWeb, otelHttp, otelHttp);
    proxy.WithUpstreamEndpoint("CLIENT_ADMIN", adminDev);

    var protofastDev = builder.AddClientApp("protofast", "../clients/protofast", protofastWeb, otelHttp, otelHttp);
    proxy.WithUpstreamEndpoint("CLIENT_PROTOFAST", protofastDev);

    var theplotDev = builder.AddClientApp("theplot", "../clients/theplot", theplotWeb, otelHttp, otelHttp);
    proxy.WithUpstreamEndpoint("CLIENT_THEPLOT", theplotDev);
}

proxy
    .WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))
    .WithUpstreamEndpoint("PAYMENTS", payments.GetEndpoint("http"))
    .WithUpstreamEndpoint("API", api.GetEndpoint("http"));

builder.Build().Run();
