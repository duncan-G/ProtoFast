namespace ProtoFast.Api.Services.Jobs;

/// <summary>Bound from <c>DirectorJobs</c>.</summary>
public sealed class DirectorJobOptions
{
    /// <summary>Jobs one api instance runs at once.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>How long a claim holds without a heartbeat before another worker may take the job over.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often a running job renews its lease and checks whether its owner cancelled it.</summary>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How often an idle worker looks for jobs other instances queued.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Claims before a job whose workers keep dying is given up on.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Finished jobs nobody dismissed are deleted after this.</summary>
    public TimeSpan KeepFinishedFor { get; set; } = TimeSpan.FromDays(7);

    public int MaxSteps { get; set; } = 400;
}
