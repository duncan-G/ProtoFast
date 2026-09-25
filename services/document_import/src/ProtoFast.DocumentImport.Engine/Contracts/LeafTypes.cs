namespace ProtoFast.DocumentImport.Engine;

// Every ref is a versioned, immutable identity. Anything resolved by ref can be cached forever.

public readonly record struct ContractRef(string SchemaId, int Version);

public readonly record struct ArtifactRef(string RunId, string StageId, string Hash)
{
    /// <summary>The stage id a run's input artifact is stored under. No stage may use it.</summary>
    public const string InputStageId = "$input";

    /// <summary>The output of an attempt that produced nothing: the executor faulted or ran out of budget.</summary>
    public static readonly ArtifactRef None = new(string.Empty, string.Empty, string.Empty);

    public bool IsNone => string.IsNullOrEmpty(Hash);
}

public readonly record struct ExecutorRef(string Id, int Version)
{
    public override string ToString() => $"{Id}@{Version}";
}

public readonly record struct PlaybookRef(string Id, int Version);

public readonly record struct WorkflowRef(string Id, int Version)
{
    /// <summary>
    /// Bucket-level outcomes are attributed to the workflow that produced the terminal output.
    /// <see cref="Outcome"/> carries an executor ref, so the workflow is named as one.
    /// </summary>
    public ExecutorRef AsExecutor() => new($"workflow:{Id}", Version);
}

public readonly record struct TraceRef(string Id);

public sealed record Budget(decimal MaxCost, TimeSpan MaxDuration)
{
    public static readonly Budget Unbounded = new(decimal.MaxValue, Timeout.InfiniteTimeSpan);
}

public sealed record Cost(decimal Amount, TimeSpan Duration)
{
    public static readonly Cost Zero = new(0, TimeSpan.Zero);
}

public sealed record Finding(string Path, string Message);

public sealed record Example(ArtifactRef Input, ArtifactRef Output);
