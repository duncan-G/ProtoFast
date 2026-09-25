namespace ProtoFast.DocumentImport.Engine;

public interface IOutcomeBus
{
    Task PublishAsync(Outcome outcome, CancellationToken ct);
}
