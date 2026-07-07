using ProtoFast.Api.Services;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.InternalAuth;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The internal-JWT public key (and any shared config) arrives Shared_-prefixed.
builder.Configuration.AddEnvironmentVariables("Shared_");
builder.Services.AddInternalJwtAuth(builder.Configuration);

// Enforce the internal JWT on every gRPC call except health probes — the edge only annotates,
// so the backend is the real authorization gate.
builder.Services.AddGrpc(options => options.Interceptors.Add<InternalJwtAuthInterceptor>());

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
