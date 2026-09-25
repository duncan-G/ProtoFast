namespace ProtoFast.DocumentImport.Engine.Executors;

public readonly record struct ExecutorRef(string Id, int Version)
{
    public override string ToString() => $"{Id}@{Version}";
}
