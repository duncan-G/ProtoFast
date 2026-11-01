using Npgsql;
using ProtoFast.DocumentImport.History;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.HistoryLoader;
using ProtoFast.ServiceDefaults;

// Fills the development history database with Project Gutenberg's American history books.
// Rerunning resumes: loaded books are skipped and downloads are cached. Options: --Loader:Limit=50,
// --Loader:Workers=4, --Loader:CacheDirectory=…
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource(HistoryDatabase.ConnectionName);

builder.Services.Configure<HistoryLoaderOptions>(builder.Configuration.GetSection("Loader"));
builder.Services.AddSingleton(sp => new HistoryDatabase(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton<HistoryCorpus>();
builder.Services.AddHttpClient<GutenbergDownloader>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(60);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("protofast-history-corpus-loader/1.0 (development corpus)");
});
builder.Services.AddSingleton<CorpusLoader>();

using var host = builder.Build();
var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
await HistorySchema.EnsureAsync(host.Services.GetRequiredService<NpgsqlDataSource>(), stopping);
await host.Services.GetRequiredService<CorpusLoader>().RunAsync(stopping);
