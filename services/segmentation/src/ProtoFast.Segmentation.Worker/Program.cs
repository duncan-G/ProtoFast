using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Data;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Routing;
using ProtoFast.Segmentation.Storage;
using ProtoFast.Segmentation.Worker;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.Secrets;

// The segmentation worker (plan §20.2). A Web host rather than a plain one purely for the gRPC
// health endpoint, so compose can probe it exactly like every other service — the worker itself
// publishes no port and is dialled by nothing. It pulls from SQS and writes to S3 and Postgres.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Shared_ carries the internal-JWT public key and anything else the platform sets for every
// service; Seg_ is this component's own prefix. Same names in dev and prod, different injector.
builder.Configuration.AddEnvironmentVariables("Shared_");
builder.Configuration.AddEnvironmentVariables("Seg_");

// Provider keys are read in-process from Secrets Manager in both environments — four API keys
// are better fetched here than staged through AppHost or .env (plan §21).
builder.Configuration.AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));

// The worker exposes no gRPC service of its own, but MapDefaultEndpoints maps the gRPC health
// service, and that needs the gRPC server stack registered.
builder.Services.AddGrpc();

builder.AddNpgsqlDataSource("segmentation");
builder.AddRedisClient("redis");

builder.Services
    .AddSegmentationDbContext()
    .AddSegmentationStorage(builder.Configuration)
    .AddSegmentationRouting(builder.Configuration)
    .AddSegmentationPipeline(builder.Configuration);

// One consumer per lane. The bulk lane exists so a ten-thousand-document import cannot starve
// interactive runs (plan §19.3).
builder.Services.AddSingleton<IHostedService>(sp => new RunConsumer(
    sp.GetRequiredService<Amazon.SQS.IAmazonSQS>(),
    sp.GetRequiredService<IWorkflowHost>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QueueOptions>>(),
    sp.GetRequiredService<ILogger<RunConsumer>>(),
    RunPriority.Realtime));

builder.Services.AddSingleton<IHostedService>(sp => new RunConsumer(
    sp.GetRequiredService<Amazon.SQS.IAmazonSQS>(),
    sp.GetRequiredService<IWorkflowHost>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QueueOptions>>(),
    sp.GetRequiredService<ILogger<RunConsumer>>(),
    RunPriority.Bulk));

builder.Services.AddHostedService<ReviewResumeConsumer>();

var app = builder.Build();

// gRPC health, so compose can probe it like every other service.
app.MapDefaultEndpoints();

app.MapGet("/", () =>
    "The segmentation worker has no HTTP surface. It consumes SQS and writes to S3 and Postgres; "
    + "this endpoint exists so a health probe has something to hit.");

app.Run();
