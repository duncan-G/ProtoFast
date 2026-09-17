using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ProtoFast.Segmentation.Data;

public static class SegmentationDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SegmentationDbContext"/> over the ambient <see cref="NpgsqlDataSource"/>
    /// (registered by Aspire.Npgsql's <c>AddNpgsqlDataSource("segmentation")</c>, dev and prod
    /// alike). Snake-case naming matches the rest of the platform; see
    /// <c>AddAuthDbContext</c> for why the shared database helper is not used.
    /// </summary>
    public static IServiceCollection AddSegmentationDbContext(this IServiceCollection services) =>
        services.AddDbContext<SegmentationDbContext>((sp, options) =>
            options
                .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
                .UseSnakeCaseNamingConvention());
}
