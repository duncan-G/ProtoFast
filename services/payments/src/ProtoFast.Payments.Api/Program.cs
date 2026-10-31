using ProtoFast.Payments.Api.Services;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.InternalAuth;

var builder = WebApplication.CreateBuilder(args);

// Its keys come from the environment's secret under ProtoStrap, and from the AppHost's
// environment variables under `aspire run`.
builder.AddServiceDefaults(stop: TimeSpan.FromSeconds(13)); // Inside the spec's stop.
builder.Services.AddInternalJwtAuth(builder.Configuration);

// Enforce the internal JWT on every gRPC call except health probes — the edge only annotates,
// so the backend is the real authorization gate (guide §6).
builder.Services.AddGrpc(options => options.Interceptors.Add<InternalJwtAuthInterceptor>());

var app = builder.Build();

app.MapDefaultEndpoints(grpc: true);

app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
