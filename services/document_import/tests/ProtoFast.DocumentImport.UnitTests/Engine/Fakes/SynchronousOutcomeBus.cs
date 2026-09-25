using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

/// <summary>Applies outcomes one at a time, as the single writer would.</summary>
internal sealed class SynchronousOutcomeBus(IServiceProvider services) : IOutcomeBus
{
    private readonly SemaphoreSlim _writer = new(1, 1);

    public ConcurrentQueue<Outcome> Published { get; } = new();

    public async Task PublishAsync(Outcome outcome, CancellationToken ct)
    {
        Published.Enqueue(outcome);
        await _writer.WaitAsync(ct);
        try
        {
            await services.GetRequiredService<IPolicyUpdater>().ApplyAsync(outcome, ct);
        }
        finally
        {
            _writer.Release();
        }
    }
}
