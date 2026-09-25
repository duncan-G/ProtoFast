namespace ProtoFast.DocumentImport.Engine;

public interface IPolicyUpdater
{
    Task ApplyAsync(Outcome outcome, CancellationToken ct);
}
