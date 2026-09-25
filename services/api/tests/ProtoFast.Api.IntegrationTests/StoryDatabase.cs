using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProtoFast.Api.IntegrationTests;
using ProtoFast.Api.Services.Screenplays;
using ProtoFast.Data.ThePlot;
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
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ThePlotDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
