using ProtoFast.AppHost.ClientApp;
using ProtoFast.AppHost.EnvoyProxy;

var builder = DistributedApplication.CreateBuilder(args);

var auth     = builder.AddProject<Projects.ProtoFast_Auth>("auth");
var payments = builder.AddProject<Projects.ProtoFast_Payments>("payments");
var api      = builder.AddProject<Projects.ProtoFast_Api>("api");

var proxy = builder.AddEnvoyProxy("envoy")
    .WaitFor(auth)
    .WaitFor(payments)
    .WaitFor(api);

var adminEndpoint = builder.AddClientApp("admin", "../clients/admin", 4000, proxy.GetEndpoint("http"));

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
