using Npgsql;
using ProtoFast.Api.Services;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Api.Services.Jobs;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Api.Services.Speech;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Screenplay;
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

// Import progress is read from the document import engine's run ledger, in the same database;
// theplot's console reads the rest of the engine through the same stores.
builder.Services.AddDurableEngineAdministration();
builder.Services.AddScoped<StoryScope>();
builder.Services.AddScoped<StoryLibrary>();
builder.Services.AddScoped<SceneSaver>();

// Director mode: the language model that directs scenes and writes sheets on request, on the
// same default provider as the import agent. The keys come from Secrets Manager under
// Api_Providers__… like the import worker's.
// Prompts, pictures and audio on GenAI spans carry the user's story, so only dev may record them.
var captureContent = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Providers:CaptureMessageContent");
builder.Services.AddLanguageModels(options =>
{
    builder.Configuration.GetSection("Providers").Bind(options);
    options.CaptureMessageContent = captureContent;
});
builder.Services.AddSingleton(builder.Configuration.GetSection("Director").Get<DirectorOptions>() ?? new DirectorOptions());
builder.Services.AddSingleton<PictureReader>();
builder.Services.AddSingleton<FrameVerifier>();
builder.Services.AddSingleton<SceneDirector>();
builder.Services.AddSingleton<SheetDescriber>();
builder.Services.AddSingleton<VoiceCaster>();
builder.Services.AddScoped<DirectionScope>();

// Audio: ElevenLabs speaks a scene's lines; its key sits with the model providers' (Api_Providers__elevenlabs__ApiKey).
builder.Services.AddSpeechSynthesis(options =>
{
    builder.Configuration.GetSection("Speech").Bind(options);
    options.ElevenLabs.ApiKey = builder.Configuration["Providers:ElevenLabs:ApiKey"];
    options.CaptureMessageContent = captureContent;
});

// Pictures: Muse draws frames and library entries; its key sits with the model providers' (Api_Providers__meta__ApiKey).
builder.Services.AddImageGeneration(options =>
{
    builder.Configuration.GetSection("Imaging").Bind(options);
    options.Muse.ApiKey = builder.Configuration["Providers:Meta:ApiKey"];
    options.CaptureMessageContent = captureContent;
});

// Model work the author asks for runs in the background off a queue in Postgres; the client polls it.
builder.Services.AddSingleton(builder.Configuration.GetSection("DirectorJobs").Get<DirectorJobOptions>() ?? new DirectorJobOptions());
builder.Services.AddSingleton<DirectorJobSignal>();
builder.Services.AddKeyedSingleton(DirectorJobStore.DataSourceKey, (_, _) =>
    NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("protofast")
                            ?? throw new InvalidOperationException("ConnectionStrings:protofast is not set.")));
builder.Services.AddSingleton<DirectorJobStore>();
builder.Services.AddSingleton<DirectorJobRunner>();
// Job steps call the service directly, outside a gRPC call.
builder.Services.AddScoped<DirectionService>();
builder.Services.AddHostedService<DirectorJobWorker>();

builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options));

// Completed uploads are handed to the document-import worker over this queue.
builder.Services.AddSqsQueue(
    DocumentImportQueues.ImportQueueKey, options => builder.Configuration.GetSection("Sqs:DocumentImport").Bind(options));

builder.AddRedisClient("redis");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<DocumentUploadService>();
app.MapGrpcService<DocumentService>();
app.MapGrpcService<StoryService>();
app.MapGrpcService<DirectionService>();
app.MapGrpcService<DirectorJobsService>();
app.MapGrpcService<AdminOverviewService>();
app.MapGrpcService<TheplotAdminService>();
app.MapGrpcService<TheplotRunsService>();
app.MapGrpcService<TheplotFamiliesService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
