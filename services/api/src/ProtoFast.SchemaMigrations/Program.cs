using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ProtoFast.Data.ThePlot;
using ProtoFast.DocumentImport.Data.Postgres;

// Standalone schema-migrations runner for the `protofast` database: ThePlot's `plot` schema and
// the document import workflow engine's `engine` schema.
// The API never migrates on boot (replicas would race); this one-shot exe owns schema changes,
// the same way ProtoFast.Auth.SchemaMigrations does for `auth`. Dev runs it via Aspire's
// WithSchemaMigrations. Exit codes: 0 ok, 1 migration error, 2 refused (--rebuild-schema in prod).

var builder = Host.CreateApplicationBuilder(args);

// Reads ConnectionStrings__protofast (dev: Aspire reference; prod: compose env).
builder.AddNpgsqlDataSource("protofast");
builder.Services.AddThePlotData();
builder.Services.AddDbContext<WorkflowEngineDbContext>((sp, options) =>
    options.UseWorkflowEngineNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ThePlotDbContext>();
var engineDb = scope.ServiceProvider.GetRequiredService<WorkflowEngineDbContext>();

var isProd = builder.Environment.IsProduction();
var rebuild = args.Contains("--rebuild-schema", StringComparer.OrdinalIgnoreCase);
if (rebuild && isProd)
{
    Console.Error.WriteLine("Refusing --rebuild-schema in Production.");
    return 2;
}

try
{
    // Serialize concurrent runners. The advisory lock is held for the connection's lifetime and
    // released when it closes below. A different constant from the auth runner's, since the two
    // databases share one Postgres and a lock id is server-wide.
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(727275);");

    if (rebuild)
    {
        // The migrations history table follows the model's default schema, so dropping the
        // schema takes it too; the public one is dropped for a history left by an older layout.
        foreach (var schema in new[] { db.Model.GetDefaultSchema() ?? "public", WorkflowEngineDbContext.Schema })
        {
            Console.WriteLine($"Rebuild: dropping schema \"{schema}\" + migrations history…");
#pragma warning disable EF1002 // interpolated identifiers are our own schema names, not user input
            await db.Database.ExecuteSqlRawAsync($@"DROP SCHEMA IF EXISTS ""{schema}"" CASCADE;");
            await db.Database.ExecuteSqlRawAsync($@"CREATE SCHEMA ""{schema}"";");
#pragma warning restore EF1002
        }

        await db.Database.ExecuteSqlRawAsync(@"DROP TABLE IF EXISTS public.""__EFMigrationsHistory"";");
    }

    Console.WriteLine("Applying migrations…");
    await db.Database.MigrateAsync();
    await engineDb.Database.MigrateAsync();
    Console.WriteLine("Migrations applied.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Migration failed: " + ex);
    return 1;
}
