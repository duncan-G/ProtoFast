namespace ProtoFast.DocumentImport.Engine.Storage;

public sealed record RunPage(IReadOnlyList<RunHeader> Runs, int Total);
