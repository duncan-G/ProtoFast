
using ProtoFast.Api;
using ProtoFast.Api.Services;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Storage;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.InternalAuth;
using ProtoFast.ServiceDefaults.Secrets;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables("Api_");
builder.Configuration.AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));
// api's own settings, including the segmentation bucket and queue URLs (plan §20.1).
builder.Services.AddInternalJwtAuth(builder.Configuration);

// Enforce the internal JWT on every gRPC call except health probes — the edge only annotates,
// so the backend is the real authorization gate.
builder.Services.AddGrpc(options => options.Interceptors.Add<InternalJwtAuthInterceptor>());

// Segmentation's read and submit surface (plan §17). api presigns, enqueues and reads Postgres;
// everything model-shaped happens in the worker.
builder.AddNpgsqlDataSource("segmentation");
builder.Services.AddSegmentationDbContext();
builder.Services.AddSegmentationStorage(
    builder.Configuration,
    storageSection: SegmentationApiOptions.SectionName,
    queueSection: SegmentationApiOptions.SectionName);
builder.Services.Configure<SegmentationApiOptions>(
    builder.Configuration.GetSection(SegmentationApiOptions.SectionName));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<GreeterService>();
app.MapGrpcService<SegmentationService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
