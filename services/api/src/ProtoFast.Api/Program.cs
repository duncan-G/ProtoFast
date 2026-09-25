using ProtoFast.Api.Services;
using ProtoFast.Data.ThePlot;
using ProtoFast.Grpc;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.InternalAuth;
using ProtoFast.ServiceDefaults.Secrets;
using ProtoFast.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables("Api_")
    .AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));

builder.Services.AddInternalJwtAuth(builder.Configuration);

// Enforce the internal JWT on every gRPC call except health probes — the edge only annotates,
// so the backend is the real authorization gate. The user-context interceptor then confines every
// query and write for the call to the subject that token names.
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<InternalJwtAuthInterceptor>();
    options.Interceptors.Add<UserContextInterceptor>();
});

builder.AddNpgsqlDataSource("protofast"); // NpgsqlDataSource for the ThePlotDbContext
builder.Services.AddThePlotData();

builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options));

builder.AddRedisClient("redis");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<DocumentUploadService>();
app.MapGrpcService<DocumentService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
