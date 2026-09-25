namespace ProtoFast.DocumentImport.Engine.Executors;

/// <summary>
/// Extra checks on agent-defined executors, e.g. that a Codified assembly builds. Throw
/// <see cref="ArgumentException"/> to reject.
/// </summary>
public interface IExecutorSpecValidator
{
    Task ValidateAsync(ExecutorSpec spec, CancellationToken ct);
}
