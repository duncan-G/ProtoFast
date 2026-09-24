using ProtoFast.Api.RateLimiting;
using ProtoFast.Api.Services;
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

builder.Services.AddGrpc(options => options.Interceptors.Add<InternalJwtAuthInterceptor>());

builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options));

builder.AddRedisClient("redis");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<DocumentUploadService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
