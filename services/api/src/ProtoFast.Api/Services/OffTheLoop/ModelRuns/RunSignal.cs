namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>Wakes this instance's worker when a run is queued or one finishes, instead of waiting out its poll.</summary>
public sealed class RunSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Wake()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already awake.
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
