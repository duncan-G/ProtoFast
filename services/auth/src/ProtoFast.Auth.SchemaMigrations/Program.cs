using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoFast.Auth.Data;

// Standalone schema-migrations runner. The auth service NEVER migrates on boot (replicas
// would race); this one-shot exe owns schema changes. Dev runs it via Aspire's
// WithSchemaMigrations; prod runs it as a one-shot compose job gated before the auth apply
// (guide §3.5). Exit codes: 0 ok, 1 migration error, 2 refused (--rebuild-schema in prod).

var builder = Host.CreateApplicationBuilder(args);

// Reads ConnectionStrings__auth (dev: Aspire reference; prod: compose env).
builder.AddNpgsqlDataSource("auth");
builder.Services.AddAuthDbContext();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

var isProd = builder.Environment.IsProduction();
var rebuild = args.Contains("--rebuild-schema", StringComparer.OrdinalIgnoreCase);
if (rebuild && isProd)
{
    Console.Error.WriteLine("Refusing --rebuild-schema in Production.");
    return 2;
}

try
{
    // Serialize concurrent runners (e.g. an auth deploy racing a manual run). The advisory
    // lock is held for the connection's lifetime and released when it closes below.
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727274);"); // app-wide constant

    if (rebuild)
    {
        var schema = db.Model.GetDefaultSchema() ?? "public";
        Console.WriteLine($"Rebuild: dropping schema \"{schema}\" + migrations history…");
#pragma warning disable EF1002 // interpolated identifiers are our own schema name, not user input
        await db.Database.ExecuteSqlRawAsync($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE;");
        await db.Database.ExecuteSqlRawAsync($@"CREATE SCHEMA ""{schema}"";");
        await db.Database.ExecuteSqlRawAsync(@"DROP TABLE IF EXISTS public.""__EFMigrationsHistory"";");
#pragma warning restore EF1002
    }

    Console.WriteLine("Applying migrations…");
    await db.Database.MigrateAsync();
    Console.WriteLine("Migrations applied.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Migration failed: " + ex);
    return 1;
}
