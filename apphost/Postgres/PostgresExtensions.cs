using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ProtoFast.AppHost.Postgres;

public static class PostgresExtensions
{
    public static IResourceBuilder<PostgresDatabaseResource> WithSchemaMigrations<TProject>(
        this IResourceBuilder<PostgresDatabaseResource> database,
        IDistributedApplicationBuilder builder)
        where TProject : IProjectMetadata, new()
    {
        var dbName = database.Resource.Name;
        var connectionName = database.Resource.DatabaseName;
        var migrations = builder.AddProject<TProject>($"{dbName}-migrations")
            .WithReference(database, connectionName)
            .WaitFor(database);

        // Prod images are built/shipped by CI pipeline, not Aspire publish.
        if (!builder.ExecutionContext.IsPublishMode)
        {
            var projectDir = Path.GetDirectoryName(new TProject().ProjectPath)!;
            migrations.WithCommand(
                "rebuild-schema",
                "Rebuild",
                ctx => ExecuteRebuildSchemaAsync(ctx, projectDir, connectionName, database),
                new CommandOptions
                {
                    IconName = "ArrowClockwise",
                    IconVariant = IconVariant.Filled,
                    IsHighlighted = true,
                    ConfirmationMessage = $"Drop the '{connectionName}' db schema and re-apply every migration?",
                });
        }

        return database;
    }

    private static async Task<ExecuteCommandResult> ExecuteRebuildSchemaAsync(
        ExecuteCommandContext context,
        string projectDir,
        string connectionName,
        IResourceBuilder<IResourceWithConnectionString> database)
    {
        var ct = context.CancellationToken;

        var connectionString = await database.Resource.GetConnectionStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(connectionString))
        {
            return CommandResults.Failure("Could not resolve the 'auth' connection string.");
        }

        // Build once so the destructive run is --no-build (fast, deterministic).
        if (await RunDotnetAsync(context, projectDir, connectionName, connectionString, ["build"], ct).ConfigureAwait(false) != 0)
        {
            return CommandResults.Failure("dotnet build of the migrations runner failed.");
        }

        var exit = await RunDotnetAsync(
            context, projectDir, connectionName, connectionString, ["run", "--no-build", "--", "--rebuild-schema"], ct)
            .ConfigureAwait(false);

        return exit == 0
            ? CommandResults.Success()
            : CommandResults.Failure($"Schema rebuild failed (exit {exit}).");
    }

    private static async Task<int> RunDotnetAsync(
        ExecuteCommandContext context, string workingDir, string connectionName, string connectionString, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        psi.Environment[$"ConnectionStrings__{connectionName}"] = connectionString;
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet.");

        var pump = Task.WhenAll(
            PumpAsync(process.StandardOutput, context, ct),
            PumpAsync(process.StandardError, context, ct));

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        await pump.ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, ExecuteCommandContext context, CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            context.Logger.LogInformation("{Line}", line);
        }
    }
}
