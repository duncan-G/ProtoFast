using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct the context offline. Generating a migration
/// only needs the model, so the connection string is a placeholder; the runner and the API
/// inject the real one (<c>ConnectionStrings__protofast</c>) at runtime.
///
/// <para>Run from <c>services/api/src/ProtoFast.Data</c>:
/// <c>dotnet ef migrations add &lt;Name&gt; --startup-project ../ProtoFast.SchemaMigrations</c>.</para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ThePlotDbContext>
{
    public ThePlotDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__protofast")
            ?? "Host=localhost;Port=5432;Database=protofast;Username=protofast;Password=protofast";

        var options = new DbContextOptionsBuilder<ThePlotDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ThePlotDbContext(options, new QueryFilterService(), new UserContext());
    }
}
