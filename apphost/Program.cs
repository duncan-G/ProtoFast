using ProtoFast.AppHost.ClientApp;
using ProtoFast.AppHost.EnvoyProxy;
using ProtoFast.AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

var otel = builder.AddOpenTelemetryCollector("otel-collector");

var auth     = builder.AddProject<Projects.ProtoFast_Auth_Api>("auth").WithOtlpCollectorReference(otel);
var payments = builder.AddProject<Projects.ProtoFast_Payments_Api>("payments").WithOtlpCollectorReference(otel);
var api      = builder.AddProject<Projects.ProtoFast_Api>("api").WithOtlpCollectorReference(otel);

var proxy = builder.AddEnvoyProxy("envoy")
    .WithOtelCollectorEndpoints(otel)
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

var adminEndpoint = builder.AddClientApp(
    "admin",
    "../clients/admin",
    4000,
    proxy.GetEndpoint("http"),
    otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName),
    otel.GetEndpoint(OpenTelemetryCollectorResource.OtlpHttpEndpointName));

proxy
    .WithCorsOriginExact(builder, adminEndpoint)
    .WithCorsOriginSubdomainRegex(builder, adminEndpoint)
    .WithAllowedHosts(builder);

proxy
    .WithClusterEndpoint(builder, "ADMIN", adminEndpoint)
    .WithClusterEndpoint(builder, "AUTH", auth.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "PAYMENTS", payments.GetEndpoint("http"))
    .WithClusterEndpoint(builder, "API", api.GetEndpoint("http"));

builder.Build().Run();
