using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoFast.Segmentation.Data;

// Standalone schema-migrations runner, mirroring ProtoFast.Auth.SchemaMigrations. Neither the
// worker nor api migrates on boot — several worker processes would race — so this one-shot exe
// owns schema changes. Dev runs it via Aspire's WithSchemaMigrations; prod runs it as a one-shot
// compose job gated before the segmentation apply. Exit codes: 0 ok, 1 migration error,
// 2 refused (--rebuild-schema in prod).

var builder = Host.CreateApplicationBuilder(args);

// Reads ConnectionStrings__segmentation (dev: Aspire reference; prod: compose env).
builder.AddNpgsqlDataSource("segmentation");
builder.Services.AddSegmentationDbContext();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

var isProd = builder.Environment.IsProduction();
var rebuild = args.Contains("--rebuild-schema", StringComparer.OrdinalIgnoreCase);
if (rebuild && isProd)
{
    Console.Error.WriteLine("Refusing --rebuild-schema in Production.");
    return 2;
}

try
{
    // Serialize concurrent runners (a segmentation deploy racing a manual run). The advisory lock
    // is held for the connection's lifetime and released when it closes below. A different
    // constant from auth's, so the two databases' migrations never block each other.
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727275);");

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
