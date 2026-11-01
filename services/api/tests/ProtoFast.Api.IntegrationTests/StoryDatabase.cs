using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProtoFast.Api.IntegrationTests;
using ProtoFast.Api.Services;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.Imaging;
using ProtoFast.Api.Services.OpenStories;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Api.Services.Speech;
using ProtoFast.Api.Services.WorldModels;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.History;
using ProtoFast.Storage.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(StoryDatabase))]

namespace ProtoFast.Api.IntegrationTests;

/// <summary>
/// One migrated Postgres for the run. Tests stay apart by each writing as a fresh user, which the
/// query filters already isolate.
/// </summary>
public sealed class StoryDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public ServiceProvider Services { get; private set; } = null!;

    /// <summary>What the api queued for the document-import worker to build.</summary>
    public RecordingQueue WorldModelQueue { get; } = new(WorldModelQueues.BuildQueueKey);

    public RecordingImageGenerator Pictures { get; } = new();

    public RecordingSpeechSynthesizer Voices { get; } = new();

    public MemoryObjectStore Objects { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        var dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        services.AddSingleton(dataSource);
        // The history tables live in their own schema, so the one database serves both.
        services.AddKeyedSingleton(HistoryDatabase.ConnectionName, dataSource);
        services.AddHistoryCorpus();
        services.AddThePlotData();
        services.AddScoped<StoryScope>();
        services.AddScoped<StoryLibrary>();
        services.AddScoped<StoryService>();
        services.AddScoped<StoryFileService>();
        services.AddKeyedSingleton<IMessageQueue>(WorldModelQueues.BuildQueueKey, WorldModelQueue);
        services.AddScoped<WorldModelBuilds>();
        services.AddScoped<OpenStoryService>();
        // The real engine tables, so the console's reads see what the ledger writes; artifacts and
        // registry content would need an object store, so those two stay in memory.
        services.AddSingleton<IArtifactStore, InMemoryArtifactStore>();
        services.AddSingleton<IRegistry, InMemoryRegistry>();
        services.AddDurableEngineAdministration();
        services.AddScoped<DocumentService>();
        services.AddScoped<AdminOverviewService>();
        services.AddScoped<TheplotAdminService>();
        services.AddScoped<TheplotRunsService>();
        services.AddScoped<TheplotFamiliesService>();
        services.AddSingleton<IImageGenerator>(Pictures);
        services.AddSingleton<IObjectStore>(Objects);
        services.AddScoped<ViewPictures>();
        services.AddSingleton<ISpeechSynthesizer>(Voices);
        services.AddScoped<TheplotWorldModelsService>();
        services.AddScoped<TheplotHistoryService>();
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().Database.MigrateAsync();
        await using var engine = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<WorkflowEngineDbContext>>()
            .CreateDbContextAsync();
        await engine.Database.MigrateAsync();
        await HistorySchema.EnsureAsync(dataSource, default);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
