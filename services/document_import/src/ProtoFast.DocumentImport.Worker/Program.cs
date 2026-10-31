using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Data.Sqs;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Worker;
using ProtoFast.DocumentImport.Worker.Briefing;
using ProtoFast.DocumentImport.Worker.Import;
using ProtoFast.ServiceDefaults;
using ProtoFast.Storage;

var builder = Host.CreateApplicationBuilder(args);

// Its keys, the model APIs' (Providers__<name>__ApiKey), come from the environment's secret
// under ProtoStrap, and from the AppHost's environment variables under `aspire run`. Stopping
// cancels imports in progress, whose messages come back once their lease lapses.
builder.AddServiceDefaults(stop: TimeSpan.FromSeconds(40));

// Both the plot schema (stories) and the engine schema (runs, policy) live in the protofast database.
builder.AddNpgsqlDataSource("protofast");
builder.Services.AddThePlotData();

// The spec's documents bucket and its two queues, from what ProtoStrap sets.
builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options), bucket: "documents");
builder.Services.AddSqsQueue(
    DocumentImportQueues.ImportQueueKey,
    options => builder.Configuration.GetSection("Sqs:DocumentImport").Bind(options),
    queue: "document-upload");
builder.Services.AddSqsQueue(
    SqsOutcomeQueue.QueueKey,
    options => builder.Configuration.GetSection("Sqs:WorkflowOutcomes").Bind(options),
    queue: "workflow-outcomes");

builder.Services.AddAgentWorkflowEngine();
builder.Services.AddDurableWorkflowEngineStores();
builder.Services.AddScreenplayDiscovery(
    options => builder.Configuration.GetSection("Providers").Bind(options),
    agent => builder.Configuration.GetSection("DiscoveryAgent").Bind(agent),
    classifier => builder.Configuration.GetSection("DocumentClassifier").Bind(classifier),
    tagger => builder.Configuration.GetSection("MentionTagger").Bind(tagger));
builder.Services.AddSkillVerification(options => builder.Configuration.GetSection("SkillVerification").Bind(options));
builder.Services.AddScriptSafetyReview(options => builder.Configuration.GetSection("ScriptSafety").Bind(options));

builder.Services.Configure<ConversionOptions>(builder.Configuration.GetSection("Conversion"));
builder.Services.AddHttpClient(ConversionClient.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddScoped<ConversionClient>();
builder.Services.AddScoped<SourceTextResolver>();
builder.Services.AddScoped<StoryWriter>();
builder.Services.AddScoped<DocumentImportRunner>();

builder.Services.Configure<DocumentImportConsumerOptions>(builder.Configuration.GetSection("Consumer"));
builder.Services.AddHostedService<DocumentImportConsumer>();

builder.Services.AddRunBriefing(options => builder.Configuration.GetSection("RunBriefing").Bind(options));
builder.Services.AddHostedService<RunBriefingService>();

builder.Build().Run();
