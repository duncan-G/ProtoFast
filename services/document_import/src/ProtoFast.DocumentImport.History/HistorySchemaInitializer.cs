using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProtoFast.DocumentImport.History;

/// <summary>Creates the history tables at startup; a missing database is logged, never fatal, since the feature is optional.</summary>
public sealed class HistorySchemaInitializer(HistoryDatabase database, ILogger<HistorySchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await HistorySchema.EnsureAsync(database.DataSource, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "The history database could not be prepared; historical stories are unavailable");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
