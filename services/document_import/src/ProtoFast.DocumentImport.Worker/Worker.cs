namespace ProtoFast.DocumentImport.Worker;

/// <summary>
/// Placeholder until the SQS consumer for document-import runs lands. The project exists so the
/// upload side and the processing side grow in the same service from the start; it is not yet
/// registered in the AppHost.
/// </summary>
public class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Delay(Timeout.Infinite, stoppingToken);
}
