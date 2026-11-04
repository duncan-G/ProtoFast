namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>Bound from <c>Runs</c>.</summary>
public sealed class RunOptions
{
    /// <summary>Runs one api instance works on at once; they mostly wait on a model.</summary>
    public int Concurrency { get; set; } = 8;

    /// <summary>How long a claim holds without a heartbeat before another worker may take the run over.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often a running run renews its lease and checks whether its owner stopped it.</summary>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How often an idle worker looks for runs other instances queued.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Claims before a run whose workers keep dying is given up on.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Finished runs nobody dismissed are deleted after this.</summary>
    public TimeSpan KeepFinishedFor { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Written text is saved at most this often; each research step is saved as it comes.</summary>
    public TimeSpan ProgressEvery { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How often a watch looks for progress.</summary>
    public TimeSpan WatchEvery { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>A watch ends after this, ahead of any proxy's stream limit; the client picks it up again.</summary>
    public TimeSpan WatchFor { get; set; } = TimeSpan.FromMinutes(4);
}
