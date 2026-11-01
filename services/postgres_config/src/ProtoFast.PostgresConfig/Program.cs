using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProtoFast.PostgresConfig;
using ProtoFast.ProtoStrap;

// postgres-config is postgres's configure task: it logs in as the superuser and makes each
// database in Databases:Names have its own user, with the password in its connection string,
// owning it. The old deploy did this with initdb scripts, which run only on an empty volume
// and never change a password. This applies the whole config every run, so it runs again
// safely after a failure, a rollback or a new password.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddProtoStrapSecret();
// A task's logs go to stdout as JSON lines, which reach telemetry however soon it exits.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = false);
using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PostgresConfig");
var configuration = builder.Configuration;

if (configuration.GetConnectionString("postgres") is not { Length: > 0 } admin)
{
    log.LogError("ConnectionStrings__postgres, the superuser's, is not set");
    return 2;
}

var databases = new List<DatabaseUser>();
foreach (var name in (configuration["Databases:Names"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    if (configuration.GetConnectionString(name) is not { Length: > 0 } connectionString)
    {
        log.LogError("ConnectionStrings__{Name} is not set", name);
        return 2;
    }

    databases.Add(DatabaseUser.From(connectionString));
}

try
{
    await using var dataSource = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" }.ConnectionString);
    var superuser = new NpgsqlConnectionStringBuilder(admin).Username ?? "postgres";
    foreach (var step in Plan.For(superuser, databases))
    {
        await step.ApplyAsync(dataSource, CancellationToken.None);
        log.LogInformation("{Step}", step.Description);
    }

    return 0;
}
catch (Exception e) when (e is NpgsqlException or PostgresException)
{
    log.LogError(e, "Configuring PostgreSQL failed");
    return 1;
}
