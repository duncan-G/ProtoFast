using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using ProtoFast.DocumentImport.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Data;

/// <summary>One disposable Postgres for the assembly, migrated with the real migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18.3").Build();

    private NpgsqlDataSource? _dataSource;

    public IDbContextFactory<WorkflowEngineDbContext> Contexts { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        var options = new DbContextOptionsBuilder<WorkflowEngineDbContext>();
        options.UseWorkflowEngineNpgsql(_dataSource);
        Contexts = new PooledDbContextFactory<WorkflowEngineDbContext>(options.Options);

        await using var db = await Contexts.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
