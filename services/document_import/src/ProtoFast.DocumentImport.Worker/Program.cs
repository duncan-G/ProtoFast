using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Data.Sqs;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Worker;
using ProtoFast.DocumentImport.Worker.Import;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.Secrets;
using ProtoFast.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Configuration
    .AddEnvironmentVariables("Shared_")
    .AddEnvironmentVariables("DocumentImport_")
    // The model API keys are stored under the api's prefix (Api_Providers__…), so that prefix is
    // the one the secret is read with.
    .AddSecretsManager(options => builder.Configuration.Bind("Secrets", options));

// Both the plot schema (stories) and the engine schema (runs, policy) live in the protofast database.
builder.AddNpgsqlDataSource("protofast");
builder.Services.AddThePlotData();

builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options));
builder.Services.AddSqsQueue(
    DocumentImportQueues.ImportQueueKey, options => builder.Configuration.GetSection("Sqs:DocumentImport").Bind(options));
builder.Services.AddSqsQueue(
    SqsOutcomeQueue.QueueKey, options => builder.Configuration.GetSection("Sqs:WorkflowOutcomes").Bind(options));

builder.Services.AddAgentWorkflowEngine();
builder.Services.AddDurableWorkflowEngineStores();
builder.Services.AddScreenplayDiscovery(
    options => builder.Configuration.GetSection("Providers").Bind(options),
    agent => builder.Configuration.GetSection("DiscoveryAgent").Bind(agent),
    classifier => builder.Configuration.GetSection("DocumentClassifier").Bind(classifier),
    tagger => builder.Configuration.GetSection("MentionTagger").Bind(tagger));

builder.Services.Configure<ConversionOptions>(builder.Configuration.GetSection("Conversion"));
builder.Services.AddHttpClient(ConversionClient.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddScoped<ConversionClient>();
builder.Services.AddScoped<SourceTextResolver>();
builder.Services.AddScoped<StoryWriter>();
builder.Services.AddScoped<DocumentImportRunner>();

builder.Services.Configure<DocumentImportConsumerOptions>(builder.Configuration.GetSection("Consumer"));
builder.Services.AddHostedService<DocumentImportConsumer>();

builder.Build().Run();
