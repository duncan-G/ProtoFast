using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// Partitions outcomes by bucket and drains each partition with one loop, so the updater is the
/// single writer for every row and bucket policy in it and no row is read-modify-written
/// concurrently.
///
/// <para>In-process and therefore not durable: outcomes still queued when the process stops are
/// lost. A durable bus (SQS FIFO with the bucket as message group) replaces this without changing
/// either side.</para>
/// </summary>
public sealed class PartitionedOutcomeBus : BackgroundService, IOutcomeBus
{
    private readonly Channel<Outcome>[] _partitions;
    private readonly IPolicyUpdater _updater;
    private readonly ILogger<PartitionedOutcomeBus> _logger;

    public PartitionedOutcomeBus(IPolicyUpdater updater, EngineOptions options, ILogger<PartitionedOutcomeBus> logger)
    {
        _updater = updater;
        _logger = logger;
        _partitions = Enumerable.Range(0, Math.Max(1, options.OutcomePartitions))
            .Select(_ => Channel.CreateUnbounded<Outcome>(new UnboundedChannelOptions { SingleReader = true }))
            .ToArray();
    }

    /// <summary>Never waits on the updater: the channel is unbounded, so this is an append.</summary>
    public Task PublishAsync(Outcome outcome, CancellationToken ct)
    {
        if (!_partitions[PartitionOf(outcome.Bucket)].Writer.TryWrite(outcome))
        {
            _logger.LogWarning("Outcome bus is closed; dropped {Kind} for {Bucket}/{StageId}",
                outcome.Kind, outcome.Bucket, outcome.StageId);
        }

        return Task.CompletedTask;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_partitions.Select(p => DrainAsync(p.Reader, stoppingToken)));

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var partition in _partitions)
        {
            partition.Writer.TryComplete();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task DrainAsync(ChannelReader<Outcome> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var outcome in reader.ReadAllAsync(ct))
            {
                try
                {
                    await _updater.ApplyAsync(outcome, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogError(e, "Policy update failed for {Kind} on {Bucket}/{StageId}",
                        outcome.Kind, outcome.Bucket, outcome.StageId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    // FNV-1a rather than string.GetHashCode, which is randomised per process: a bucket keeps its
    // partition across restarts, which a durable replacement will rely on.
    private int PartitionOf(string bucket)
    {
        var hash = 2166136261u;
        foreach (var c in bucket)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash % (uint)_partitions.Length);
    }
}
