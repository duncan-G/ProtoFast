using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProtoFast.Auth.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct the context offline. Generating a migration
/// only needs the model, so the connection string is a placeholder; the runner and service
/// inject the real one (<c>ConnectionStrings__auth</c>) at runtime.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__auth")
            ?? "Host=localhost;Port=5432;Database=auth;Username=auth;Password=auth";

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AuthDbContext(options);
    }
}
