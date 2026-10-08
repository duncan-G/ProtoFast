namespace ProtoFast.DocumentImport.Worker;

public sealed class DocumentImportConsumerOptions
{
    /// <summary>Imports are mostly waiting on model calls, so a worker runs several at once.</summary>
    public int MaxConcurrentImports { get; set; } = 8;
}
