using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ProtoFast.DocumentImport.Data;

public static class WorkflowEngineDbContextOptions
{
    public static DbContextOptionsBuilder<WorkflowEngineDbContext> UseWorkflowEngineNpgsql(
        this DbContextOptionsBuilder<WorkflowEngineDbContext> builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, UseEngineHistoryTable)
            .UseSnakeCaseNamingConvention();

    public static DbContextOptionsBuilder UseWorkflowEngineNpgsql(this DbContextOptionsBuilder builder, DbDataSource dataSource) =>
        builder
            .UseNpgsql(dataSource, UseEngineHistoryTable)
            .UseSnakeCaseNamingConvention();

    // ThePlot shares the database, so each context keeps its history table in its own schema.
    private static void UseEngineHistoryTable(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WorkflowEngineDbContext.Schema);
}
