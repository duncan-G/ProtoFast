namespace ProtoFast.DocumentImport.Worker;

public sealed class DocumentImportConsumerOptions
{
    /// <summary>Imports are mostly waiting on model calls, so a worker runs several at once.</summary>
    public int MaxConcurrentImports { get; set; } = 8;

    /// <summary>How often a running import checks whether its owner cancelled it.</summary>
    public TimeSpan CancellationPollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
