using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ProtoFast.Auth.Data;

public static class AuthDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AuthDbContext"/> over the ambient <see cref="NpgsqlDataSource"/>
    /// (registered by Aspire.Npgsql's <c>AddNpgsqlDataSource("auth")</c>, dev + prod alike).
    /// Snake-case naming matches the rest of the platform. Deliberately avoids the shared
    /// <c>AddCoreDatabaseServices</c> helper, which hard-wires pgvector (guide §3.5.1).
    /// </summary>
    public static IServiceCollection AddAuthDbContext(this IServiceCollection services) =>
        services.AddDbContext<AuthDbContext>((sp, options) =>
            options
                .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
                .UseSnakeCaseNamingConvention());
}
