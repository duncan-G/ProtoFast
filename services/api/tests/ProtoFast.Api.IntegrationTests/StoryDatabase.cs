using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProtoFast.Api.IntegrationTests;
using ProtoFast.Api.Services;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Api.Services.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop.ModelRuns;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.OffTheLoop;
using ProtoFast.Data.ThePlot;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Storage;
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

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddSingleton(NpgsqlDataSource.Create(_container.GetConnectionString()));
        services.AddThePlotData();
        services.AddScoped<StoryScope>();
        services.AddScoped<StoryLibrary>();
        services.AddScoped<StoryService>();
        services.AddScoped<StoryFileService>();
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
        services.AddOffTheLoopData();
        services.AddScoped<CompanyService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<NoteService>();
        services.AddScoped<TodoService>();
        services.AddScoped<GoalService>();
        services.AddLogging();
        services.AddSingleton<StoredFiles>();
        services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<StoredFiles>());
        services.AddSingleton<IPresignedUrlFactory>(sp => sp.GetRequiredService<StoredFiles>());
        services.AddScoped<LoopDocumentService>();
        // Runs are claimed and run by the tests themselves, with no worker in the background.
        services.AddSingleton(new RunOptions());
        services.AddSingleton<RunSignal>();
        services.AddKeyedSingleton(RunStore.DataSourceKey, (_, _) => NpgsqlDataSource.Create(_container.GetConnectionString()));
        services.AddSingleton<RunStore>();
        services.AddSingleton<RunExecutor>();
        services.AddScoped<RunsService>();
        services.AddSingleton<ScriptedModels>();
        services.AddScoped(sp => sp.GetRequiredService<ScriptedModels>().ResearchFor(sp.GetRequiredService<UserContext>().CurrentUserId));
        services.AddScoped(sp => sp.GetRequiredService<ScriptedModels>().WritingFor(sp.GetRequiredService<UserContext>().CurrentUserId));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PostedPages>();
        services.AddSingleton(sp => new PostingPages(
            sp.GetRequiredService<PostedPages>(), sp.GetRequiredService<ILogger<PostingPages>>()));
        services.AddScoped<ResearcherService>();
        services.AddScoped<WriterService>();
        services.AddScoped<ResumeService>();
        services.AddScoped<ResumeWriterService>();
        services.AddScoped<ResumeFiles>();
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<OffTheLoopDbContext>().Database.MigrateAsync();
        await using var engine = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<WorkflowEngineDbContext>>()
            .CreateDbContextAsync();
        await engine.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
