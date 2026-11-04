using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ProtoFast.Database;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop;

/// <summary>
/// For <c>dotnet ef</c>, run from <c>services/api/src/ProtoFast.Data</c>:
/// <c>dotnet ef migrations add &lt;Name&gt; --context OffTheLoopDbContext -o OffTheLoop/Migrations --startup-project ../ProtoFast.SchemaMigrations</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OffTheLoopDbContext>
{
    public OffTheLoopDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__protofast")
            ?? "Host=localhost;Port=5432;Database=protofast;Username=protofast;Password=protofast";

        var options = new DbContextOptionsBuilder<OffTheLoopDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OffTheLoopDbContext(options, new QueryFilterService(), new UserContext());
    }
}
