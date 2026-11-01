using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoFast.ProtoStrap;

namespace ProtoFast.ServiceDefaults;

/// <summary>DrainState says whether the replica is stopping: its readiness fails from then on.</summary>
public sealed class DrainState
{
    private volatile bool _draining;

    public bool Draining => _draining;

    internal void Start() => _draining = true;
}

/// <summary>
/// Drain fails readiness at SIGTERM, then keeps serving for DRAIN_SECONDS (10 under
/// ProtoStrap, 0 locally), so the edge stops sending before connections close. StoppingAsync
/// runs before any hosted service stops, so Kestrel keeps serving meanwhile.
/// </summary>
public sealed class Drain(DrainState state, ILogger<Drain> log) : IHostedLifecycleService
{
    public static TimeSpan Delay =>
        TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("DRAIN_SECONDS"), out var s) ? s
            : Replica.UnderProtoStrap ? 10 : 0);

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        state.Start();
        var delay = Delay;
        log.LogInformation("Stopping: not ready, serving on for {Seconds} s", delay.TotalSeconds);
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
