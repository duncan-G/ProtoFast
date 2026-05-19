using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public static class DatabaseServiceCollectionExtensions
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1"];

    public static IServiceCollection AddQueryFactory<TEntity, TQuery, TQueryImplementation>(this IServiceCollection services)
        where TQuery : IQuery<TEntity>
        where TQueryImplementation : class, TQuery
    {
        services.AddScoped<IQueryFactory<TEntity, TQuery>, QueryFactory<TEntity, TQueryImplementation>>();
        return services;
    }

    public static IServiceCollection AddCoreDatabaseServices<TContext>(
        this IServiceCollection services,
        Action<DatabaseOptions> configureOptions) where TContext : DbContext, IDbContext
    {
        services
            .Configure(configureOptions)
            .AddDbContext<TContext>((sp, options) =>
            {
                var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
                options.UseNpgsql(
                        dataSource,
                        npgsqlOptions => npgsqlOptions.UseVector().CommandTimeout(dbOptions.CommandTimeout))
                    .UseSnakeCaseNamingConvention();
            })
            .AddScoped<UserContext>()
            .AddScoped<QueryFilterService>()
            .AddScoped<PagingTokenHelper>()
            .AddScoped<IUnitOfWorkFactory>(sp =>
            {
                TContext dbContext = sp.GetRequiredService<TContext>();
                return new UnitOfWorkFactory(dbContext);
            });

        return services;
    }
}
