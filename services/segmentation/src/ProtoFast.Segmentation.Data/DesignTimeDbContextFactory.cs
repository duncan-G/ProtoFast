using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProtoFast.Segmentation.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct the context offline. Generating a migration only
/// needs the model, so the connection string is a placeholder; the runner and the services inject
/// the real one (<c>ConnectionStrings__segmentation</c>) at runtime.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SegmentationDbContext>
{
    public SegmentationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__segmentation")
            ?? "Host=localhost;Port=5432;Database=segmentation;Username=segmentation;Password=segmentation";

        var options = new DbContextOptionsBuilder<SegmentationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SegmentationDbContext(options);
    }
}
