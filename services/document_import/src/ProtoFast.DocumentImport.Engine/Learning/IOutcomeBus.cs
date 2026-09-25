namespace ProtoFast.DocumentImport.Engine.Learning;

public interface IOutcomeBus
{
    Task PublishAsync(Outcome outcome, CancellationToken ct);
}
