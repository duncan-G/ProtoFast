using System.Security.Cryptography;
using ProtoFast.AppHost.ClientApp;
using ProtoFast.AppHost.EnvoyProxy;
using ProtoFast.AppHost.OpenTelemetryCollector;
using ProtoFast.AppHost.Postgres;

var builder = DistributedApplication.CreateBuilder(args);


// The unified SSR host serves every client in publish mode. Set SsrHost__Dev=true
// (or run with --SsrHost:Dev=true) to smoke-test it locally instead of per-client
// dev servers — same Envoy listener URLs, no HMR.
var useSsrHost = builder.ExecutionContext.IsPublishMode
    || bool.TryParse(builder.Configuration["SsrHost:Dev"], out var ssrHostDev) && ssrHostDev;

var (internalJwtPrivateKeyPem, internalJwtPublicKeyPem) = GenerateInternalJwtKeyPair();

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

var redis = builder.AddRedis("redis");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithImageTag("26.6")
    .WithRealmImport("../infra/keycloak/realms")
    // Enable ${env.VAR:default} placeholder substitution in the realm import files.
    // Without this, Keycloak imports the literal defaults (e.g. SMTP host localhost:1025)
    // instead of the SMTP_* env vars we inject below.
    .WithEnvironment("JAVA_OPTS_APPEND", "-Dkeycloak.migration.replace-placeholders=true")
    // Custom "protofast" login theme (referenced by loginTheme in the realm import).
    // start-dev disables theme caching, so edits under this dir show up on refresh.
    .WithBindMount("../infra/keycloak/themes", "/opt/keycloak/themes", isReadOnly: true)
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

if (!builder.ExecutionContext.IsPublishMode)
{
    var mailEndpoint = builder.AddContainer("smtp4dev", "rnwood/smtp4dev")
        .WithHttpEndpoint(targetPort: 80, name: "web")
        .WithEndpoint(targetPort: 25, name: "smtp")
        .GetEndpoint("smtp", KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

    keycloak
        .WithEnvironment("SMTP_HOST", mailEndpoint.Property(EndpointProperty.Host))
        .WithEnvironment("SMTP_PORT", mailEndpoint.Property(EndpointProperty.Port))
        .WithEnvironment("SMTP_FROM", "no-reply@protofast.dev");
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
    .WithEnvironment("Auth_InternalJwt__PrivateKeyPem", internalJwtPrivateKeyPem);

// Payments
var payments = builder.AddProject<Projects.ProtoFast_Payments_Api>("payments")
    .WithOtlpCollectorReference(otel)
    .WithEnvironment("Shared_InternalJwt__PublicKeyPem", internalJwtPublicKeyPem);

// Api
var api = builder.AddProject<Projects.ProtoFast_Api>("api")
    .WithOtlpCollectorReference(otel)
    .WithEnvironment("Shared_InternalJwt__PublicKeyPem", internalJwtPublicKeyPem);

// Envoy Proxy
var proxy = builder.AddEnvoyProxy("envoy", useSsrHost)
    .WithOtelCollectorEndpoints(otel)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

var otelHttp = otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName);

// Clients: each gets its own Envoy listener (dev) or domain virtual host (publish).
var adminWeb = proxy.WithClient(builder, "admin");
var protofastWeb = proxy.WithClient(builder, "protofast");

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
}

proxy
    .WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))
    .WithUpstreamEndpoint("PAYMENTS", payments.GetEndpoint("http"))
    .WithUpstreamEndpoint("API", api.GetEndpoint("http"));

builder.Build().Run();

static (string PrivatePem, string PublicPem) GenerateInternalJwtKeyPair()
{
    using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    return (ec.ExportPkcs8PrivateKeyPem(), ec.ExportSubjectPublicKeyInfoPem());
}
