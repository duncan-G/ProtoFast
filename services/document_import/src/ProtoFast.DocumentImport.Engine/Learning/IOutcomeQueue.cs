namespace ProtoFast.DocumentImport.Engine.Learning;

public interface IOutcomeQueue
{
    Task PublishAsync(Outcome outcome, CancellationToken ct);
}
