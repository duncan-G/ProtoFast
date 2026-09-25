namespace ProtoFast.DocumentImport.Engine.Learning;

public interface IPolicyUpdater
{
    Task ApplyAsync(Outcome outcome, CancellationToken ct);
}
