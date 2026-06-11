using ProtoFast.AppHost.ClientApp;
using ProtoFast.AppHost.EnvoyProxy;
using ProtoFast.AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

var otel = builder.AddOpenTelemetryCollector("otel-collector");

var auth     = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth").WithOtlpCollectorReference(otel);
var payments = builder.AddProject<Projects.ProtoFast_Payments_Api>("payments").WithOtlpCollectorReference(otel);
var api      = builder.AddProject<Projects.ProtoFast_Api>("api").WithOtlpCollectorReference(otel);

// The unified SSR host serves every client in publish mode. Set SsrHost__Dev=true
// (or run with --SsrHost:Dev=true) to smoke-test it locally instead of per-client
// dev servers — same Envoy listener URLs, no HMR.
var useSsrHost = builder.ExecutionContext.IsPublishMode
    || bool.TryParse(builder.Configuration["SsrHost:Dev"], out var ssrHostDev) && ssrHostDev;

var proxy = builder.AddEnvoyProxy("envoy", useSsrHost)
    .WithOtelCollectorEndpoints(otel)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

var otelHttp = otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName);

// Clients: each gets its own Envoy listener (dev) or domain virtual host (publish).
var adminWeb = proxy.WithClient(builder, "admin");

if (useSsrHost)
{
    var clientsHost = builder.AddClientHost("clients", defaultClient: "admin", otelHttp, otelHttp);
    proxy
        .WithUpstreamEndpoint("CLIENTS_HOST", clientsHost)
        .WithEnvironment("DEFAULT_CLIENT", "admin");
}
else
{
    var adminDev = builder.AddClientApp("admin", "../clients/admin", adminWeb, otelHttp, otelHttp);
    proxy.WithUpstreamEndpoint("CLIENT_ADMIN", adminDev);
}

proxy
    .WithUpstreamEndpoint("AUTH", auth.GetEndpoint("http"))
    .WithUpstreamEndpoint("PAYMENTS", payments.GetEndpoint("http"))
    .WithUpstreamEndpoint("API", api.GetEndpoint("http"));

builder.Build().Run();
