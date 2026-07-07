namespace ProtoFast.Auth.Api.Correlation;

public interface ICorrelationStore
{
    Task SaveAsync(string state, CorrelationData data, CancellationToken ct = default);

    /// <summary>Reads and removes the correlation in one step (single-use). A null result means an
    /// unknown or already-consumed <c>state</c> — reject the callback.</summary>
    Task<CorrelationData?> TakeAsync(string state, CancellationToken ct = default);
}
