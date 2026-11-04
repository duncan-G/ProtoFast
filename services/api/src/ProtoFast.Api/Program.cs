using ProtoFast.Api.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop.ModelRuns;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Api.Services.Screenplays.Files;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data;
using ProtoFast.Grpc;
using ProtoFast.ServiceDefaults;
using ProtoFast.ServiceDefaults.InternalAuth;
using ProtoFast.Storage;

var builder = WebApplication.CreateBuilder(args);

// Its keys come from the environment's secret under ProtoStrap, and from the AppHost's
// environment variables under `aspire run`.
builder.AddServiceDefaults(stop: TimeSpan.FromSeconds(28)); // Inside the spec's stop.

builder.Services.AddInternalJwtAuth(builder.Configuration);

// Enforce the internal JWT on every gRPC call except health probes — the edge only annotates,
// so the backend is the real authorization gate. The user-context interceptor then confines every
// query and write for the call to the subject that token names.
builder.Services.AddGrpc(options =>
    {
        options.Interceptors.Add<InternalJwtAuthInterceptor>();
        options.Interceptors.Add<UserContextInterceptor>();
    })
    // A whole .theplot file arrives in one message; 1 KiB covers its framing.
    .AddServiceOptions<StoryFileService>(options => options.MaxReceiveMessageSize = TheplotJson.MaxBytes + 1024);

builder.AddNpgsqlDataSource("protofast"); // NpgsqlDataSource for ThePlot's and OffTheLoop's contexts
builder.Services.AddThePlotData();
builder.Services.AddOffTheLoopData();
builder.Services.TryAddSingleton(TimeProvider.System);

// OffTheLoop's notes write with DeepSeek, and its companies and job postings are researched with
// Gemini grounded in Google Search.
builder.Services.Configure<ModelProviders>(builder.Configuration.GetSection("Providers"));
builder.Services.Configure<WriterOptions>(builder.Configuration.GetSection("Writer"));
builder.Services.AddSingleton<ModelCalls>();
builder.Services.AddSingleton<IWritingModel, DeepSeekWritingModel>();
builder.Services.AddHttpClient(GeminiResearchModel.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddSingleton<IResearchModel, GeminiResearchModel>();
// A posting's own text is fetched off its page, from public addresses only, for 4 MiB at most.
builder.Services.AddHttpClient(PostingPages.HttpClientName, http =>
    {
        http.Timeout = TimeSpan.FromSeconds(20);
        http.MaxResponseContentBufferSize = 4 * 1024 * 1024;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; OffTheLoop/1.0)");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
    })
    .ConfigurePrimaryHttpMessageHandler(PublicAddresses.Handler);
builder.Services.AddSingleton<PostingPages>();

// The writer's and researcher's work runs in the background off a queue in Postgres; the client watches it.
builder.Services.AddSingleton(builder.Configuration.GetSection("Runs").Get<RunOptions>() ?? new RunOptions());
builder.Services.AddSingleton<RunSignal>();
builder.Services.AddKeyedSingleton(RunStore.DataSourceKey, (_, _) =>
    NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("protofast")
                            ?? throw new InvalidOperationException("ConnectionStrings:protofast is not set.")));
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<RunExecutor>();
// Runs call the services directly, outside a gRPC call.
builder.Services.AddScoped<WriterService>();
builder.Services.AddScoped<ResearcherService>();
builder.Services.AddHostedService<RunWorker>();

// Import progress is read from the document import engine's run ledger, in the same database;
// theplot's console reads the rest of the engine through the same stores.
builder.Services.AddDurableEngineAdministration();
builder.Services.AddScoped<StoryScope>();
builder.Services.AddScoped<StoryLibrary>();

// The spec's documents bucket and document-upload queue, from what ProtoStrap sets.
builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options), bucket: "documents");

// Completed uploads are handed to the document-import worker over this queue.
builder.Services.AddSqsQueue(
    DocumentImportQueues.ImportQueueKey,
    options => builder.Configuration.GetSection("Sqs:DocumentImport").Bind(options),
    queue: "document-upload");

builder.AddRedisClient("redis");

var app = builder.Build();

app.MapDefaultEndpoints(grpc: true);

app.MapGrpcService<DocumentUploadService>();
app.MapGrpcService<DocumentService>();
app.MapGrpcService<StoryService>();
app.MapGrpcService<StoryFileService>();
app.MapGrpcService<CompanyService>();
app.MapGrpcService<ApplicationService>();
app.MapGrpcService<NoteService>();
app.MapGrpcService<WriterService>();
app.MapGrpcService<ResearcherService>();
app.MapGrpcService<RunsService>();
app.MapGrpcService<TodoService>();
app.MapGrpcService<GoalService>();
app.MapGrpcService<LoopDocumentService>();
app.MapGrpcService<AdminOverviewService>();
app.MapGrpcService<TheplotAdminService>();
app.MapGrpcService<TheplotRunsService>();
app.MapGrpcService<TheplotFamiliesService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
