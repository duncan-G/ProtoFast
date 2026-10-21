using ProtoFast.Api.Services;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Api.Services.OpenStories;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Api.Services.Speech;
using ProtoFast.Api.Services.WorldModels;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.History;
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
builder.Services.AddScoped<WorldModelBuilds>();
builder.Services.AddScoped<ViewPictures>();

builder.Services.AddS3ObjectStorage(options => builder.Configuration.GetSection("S3").Bind(options));

// Completed uploads are handed to the document-import worker over this queue, and world model
// builds over the next one.
builder.Services.AddSqsQueue(
    DocumentImportQueues.ImportQueueKey, options => builder.Configuration.GetSection("Sqs:DocumentImport").Bind(options));
builder.Services.AddSqsQueue(
    WorldModelQueues.BuildQueueKey, options => builder.Configuration.GetSection("Sqs:WorldModelBuild").Bind(options));

builder.AddRedisClient("redis");

// theplot's console pictures a world's views and speaks its beats; the keys sit with the model providers'
// (Api_Providers__meta__ApiKey, Api_Providers__elevenlabs__ApiKey).
// Prompts, pictures and audio on GenAI spans carry the user's story, so only dev may record them.
var captureContent = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Providers:CaptureMessageContent");
var muse = builder.Configuration.GetSection("Imaging:Muse").Get<MuseImageOptions>() ?? new MuseImageOptions();
muse.ApiKey = builder.Configuration["Providers:Meta:ApiKey"];
builder.Services.AddSingleton(muse);
// Muse refines over several passes, so a picture can take minutes.
builder.Services.AddHttpClient<MuseImageGenerator>(http => http.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddTransient<IImageGenerator>(sp => new TracedImageGenerator(
    sp.GetRequiredService<MuseImageGenerator>(), "meta", muse.Model, new Uri(muse.BaseUrl).Host, captureContent));
var elevenLabs = builder.Configuration.GetSection("Speech:ElevenLabs").Get<ElevenLabsSpeechOptions>() ?? new ElevenLabsSpeechOptions();
elevenLabs.ApiKey = builder.Configuration["Providers:ElevenLabs:ApiKey"];
builder.Services.AddHttpClient(ElevenLabsSpeechSynthesizer.HttpClientName);
builder.Services.AddSingleton<ISpeechSynthesizer>(sp => new TracedSpeechSynthesizer(
    new ElevenLabsSpeechSynthesizer(
        elevenLabs, sp.GetRequiredService<IHttpClientFactory>().CreateClient(ElevenLabsSpeechSynthesizer.HttpClientName)),
    "elevenlabs", elevenLabs.Model, new Uri(elevenLabs.BaseUrl).Host, captureContent));

// Stories written from the history corpus: development only, and only with its database attached.
var historyStories = builder.Environment.IsDevelopment()
    && builder.Configuration.GetConnectionString(HistoryDatabase.ConnectionName) is { Length: > 0 };
if (historyStories)
{
    builder.AddKeyedNpgsqlDataSource(HistoryDatabase.ConnectionName);
    builder.Services.AddHistoryCorpus();
}

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGrpcService<DocumentUploadService>();
app.MapGrpcService<DocumentService>();
app.MapGrpcService<StoryService>();
app.MapGrpcService<OpenStoryService>();
app.MapGrpcService<AdminOverviewService>();
app.MapGrpcService<TheplotAdminService>();
app.MapGrpcService<TheplotRunsService>();
app.MapGrpcService<TheplotFamiliesService>();
app.MapGrpcService<TheplotWorldModelsService>();
if (historyStories)
{
    app.MapGrpcService<TheplotHistoryService>();
}

app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
