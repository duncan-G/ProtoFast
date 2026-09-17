using ProtoFast.Payments.Api.Services;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.InternalAuth;
using ProtoFast.ServiceDefaults.Secrets;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables("Payments_");
builder.Configuration.AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));
builder.Services.AddInternalJwtAuth(builder.Configuration);

// Enforce the internal JWT on every gRPC call except health probes — the edge only annotates,
// so the backend is the real authorization gate (guide §6).
builder.Services.AddGrpc(options => options.Interceptors.Add<InternalJwtAuthInterceptor>());

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
